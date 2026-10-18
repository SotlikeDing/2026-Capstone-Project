using Microsoft.Data.SqlClient;
using WebAPI.Models;

namespace WebAPI.Database;

/// <summary>
/// Real document upload/management for PJ, PH, VG and Farmer roles. Every write is
/// mirrored into the immutable documentAuditLog (see db/AddUserDocumentManagement.sql)
/// so who uploaded/replaced/deleted what, and when, can never be silently lost.
/// </summary>
public class UserDocuments_db
{
    private readonly string _connectionString;
    public UserDocuments_db(string connectionString) => _connectionString = connectionString;

    /// <summary>Which document types this role needs, and whether each is mandatory.</summary>
    public virtual async Task<List<DocumentRequirementDto>> GetRequiredDocumentTypes(string roleCode)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        const string q = @"SELECT dt.dtId, dt.dtName, rdr.isRequired
                            FROM roleDocumentRequirements rdr
                            JOIN documentType dt ON dt.dtId = rdr.documentTypeId
                            WHERE rdr.roleCode = @roleCode
                            ORDER BY dt.dtName";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@roleCode", roleCode);
        using var r = await cmd.ExecuteReaderAsync();

        var list = new List<DocumentRequirementDto>();
        while (await r.ReadAsync())
        {
            list.Add(new DocumentRequirementDto
            {
                DocumentTypeId = r.GetInt32(0),
                DocumentTypeName = r.GetString(1),
                IsRequired = r.GetBoolean(2)
            });
        }
        return list;
    }

    /// <summary>Full active documents for a role instance, file data included - owner/admin only.</summary>
    public virtual async Task<List<UserDocumentDto>> GetDocumentsForUserRole(int userRoleId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        const string q = @"SELECT ud.udId, ud.udUserRole, ud.udDtId, dt.dtName, ud.udName, ud.udFilePath,
                                   ud.udUploadedTimestamp, ud.udUploadedBy, ISNULL(u.uName + ' ' + u.uSurname, ''),
                                   CASE WHEN rdr.isRequired = 1 THEN 1 ELSE 0 END AS isRequired,
                                   ud.udStatus, ud.udVerifiedAt, ISNULL(vu.uName + ' ' + vu.uSurname, NULL), ud.udRejectionReason
                            FROM userDocumentation ud
                            JOIN documentType dt ON dt.dtId = ud.udDtId
                            JOIN userRoles ur ON ur.userRoleId = ud.udUserRole
                            JOIN roles ro ON ro.roleId = ur.urRoleId
                            LEFT JOIN users u ON u.userId = ud.udUploadedBy
                            LEFT JOIN users vu ON vu.userId = ud.udVerifiedBy
                            LEFT JOIN roleDocumentRequirements rdr ON rdr.roleCode = ro.roleCode AND rdr.documentTypeId = ud.udDtId
                            WHERE ud.udUserRole = @userRoleId AND ud.udIsDeleted = 0
                            ORDER BY dt.dtName";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@userRoleId", userRoleId);
        using var r = await cmd.ExecuteReaderAsync();

        var list = new List<UserDocumentDto>();
        while (await r.ReadAsync())
        {
            list.Add(new UserDocumentDto
            {
                Id = r.GetInt32(0),
                UserRoleId = r.GetInt32(1),
                DocumentTypeId = r.GetInt32(2),
                DocumentTypeName = r.GetString(3),
                FileName = r.IsDBNull(4) ? "" : r.GetString(4),
                FileDataUrl = r.IsDBNull(5) ? "" : r.GetString(5),
                UploadedAt = r.IsDBNull(6) ? DateTime.MinValue : r.GetDateTime(6),
                UploadedByUserId = r.IsDBNull(7) ? null : r.GetInt32(7),
                UploadedByName = r.GetString(8),
                IsRequired = r.GetInt32(9) == 1,
                Status = r.IsDBNull(10) ? "Pending" : r.GetString(10),
                VerifiedAt = r.IsDBNull(11) ? null : r.GetDateTime(11),
                VerifiedByName = r.IsDBNull(12) ? null : r.GetString(12),
                RejectionReason = r.IsDBNull(13) ? null : r.GetString(13)
            });
        }
        return list;
    }

    /// <summary>
    /// Public-safe view for cross-role profile viewing: whether each required/optional
    /// credential exists and when it was uploaded - the file itself is never included.
    /// </summary>
    public virtual async Task<List<UserDocumentSummaryDto>> GetDocumentSummaryForUserRole(int userRoleId, string roleCode)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        const string q = @"SELECT dt.dtId, dt.dtName, rdr.isRequired, ud.udId, ud.udUploadedTimestamp, ud.udStatus
                            FROM roleDocumentRequirements rdr
                            JOIN documentType dt ON dt.dtId = rdr.documentTypeId
                            OUTER APPLY (
                                SELECT TOP 1 udId, udUploadedTimestamp, udStatus
                                FROM userDocumentation
                                WHERE udUserRole = @userRoleId AND udDtId = rdr.documentTypeId AND udIsDeleted = 0
                                ORDER BY udUploadedTimestamp DESC
                            ) ud
                            WHERE rdr.roleCode = @roleCode
                            ORDER BY dt.dtName";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@userRoleId", userRoleId);
        cmd.Parameters.AddWithValue("@roleCode", roleCode);
        using var r = await cmd.ExecuteReaderAsync();

        var list = new List<UserDocumentSummaryDto>();
        while (await r.ReadAsync())
        {
            list.Add(new UserDocumentSummaryDto
            {
                DocumentTypeId = r.GetInt32(0),
                DocumentTypeName = r.GetString(1),
                IsRequired = r.GetBoolean(2),
                IsUploaded = !r.IsDBNull(3),
                UploadedAt = r.IsDBNull(4) ? null : r.GetDateTime(4),
                Status = r.IsDBNull(5) ? null : r.GetString(5)
            });
        }
        return list;
    }

    /// <summary>
    /// Atomic upsert keyed on (userRoleId, documentTypeId): if an active document of
    /// this type already exists for this role, it's replaced in place rather than a
    /// second row being inserted. MERGE ... WITH (HOLDLOCK) closes a real race - two
    /// concurrent uploads for a still-empty slot (double-submit, two open tabs) would
    /// otherwise both see "no existing document" and both insert, leaving a duplicate
    /// that a required document type can never be cleaned up (required docs can only
    /// be replaced, never bare-deleted). Wrapped in a transaction with the audit write
    /// so the two can never diverge (a document change with no matching audit entry).
    /// </summary>
    public virtual async Task<int> UploadDocument(int userRoleId, int documentTypeId, string fileName, string fileDataUrl, int uploadedByUserId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var tran = (SqlTransaction)await conn.BeginTransactionAsync();

        try
        {
            const string typeNameQ = "SELECT dtName FROM documentType WHERE dtId = @dtId";
            using var typeNameCmd = new SqlCommand(typeNameQ, conn, tran);
            typeNameCmd.Parameters.AddWithValue("@dtId", documentTypeId);
            var typeNameResult = await typeNameCmd.ExecuteScalarAsync();
            if (typeNameResult == null)
                throw new InvalidOperationException("Onbekende dokumenttipe.");
            string typeName = typeNameResult.ToString()!;

            const string mergeQ = @"
                MERGE INTO userDocumentation WITH (HOLDLOCK) AS target
                USING (SELECT @userRoleId AS udUserRole, @dtId AS udDtId) AS src
                  ON target.udUserRole = src.udUserRole AND target.udDtId = src.udDtId AND target.udIsDeleted = 0
                WHEN MATCHED THEN
                    UPDATE SET udName = @name, udFilePath = @fileData, udUploadedTimestamp = SYSUTCDATETIME(), udUploadedBy = @uploadedBy,
                               udStatus = 'Pending', udVerifiedBy = NULL, udVerifiedAt = NULL, udRejectionReason = NULL
                WHEN NOT MATCHED THEN
                    INSERT (udUserRole, udName, udFilePath, udUploadedTimestamp, udStatus, udDtId, udUploadedBy, udIsDeleted)
                    VALUES (src.udUserRole, @name, @fileData, SYSUTCDATETIME(), 'Pending', src.udDtId, @uploadedBy, 0)
                OUTPUT inserted.udId, $action;";
            using var mergeCmd = new SqlCommand(mergeQ, conn, tran);
            mergeCmd.Parameters.AddWithValue("@userRoleId", userRoleId);
            mergeCmd.Parameters.AddWithValue("@dtId", documentTypeId);
            mergeCmd.Parameters.AddWithValue("@name", fileName);
            mergeCmd.Parameters.AddWithValue("@fileData", fileDataUrl);
            mergeCmd.Parameters.AddWithValue("@uploadedBy", uploadedByUserId);

            int newId;
            string action;
            using (var r = await mergeCmd.ExecuteReaderAsync())
            {
                await r.ReadAsync();
                newId = r.GetInt32(0);
                action = r.GetString(1);
            }

            string actionType = action == "INSERT" ? "Created" : "Replaced";
            await WriteAuditEntry(conn, tran, newId, userRoleId, uploadedByUserId, actionType, typeName, fileName);

            await tran.CommitAsync();
            return newId;
        }
        catch
        {
            await tran.RollbackAsync();
            throw;
        }
    }

    /// <summary>Replaces a document's file in place, resetting its verification status to Pending.</summary>
    public virtual async Task<bool> ReplaceDocument(int documentId, string fileName, string fileDataUrl, int actorUserId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var tran = (SqlTransaction)await conn.BeginTransactionAsync();

        try
        {
            const string infoQ = @"SELECT ud.udUserRole, dt.dtName
                                    FROM userDocumentation ud
                                    JOIN documentType dt ON dt.dtId = ud.udDtId
                                    WHERE ud.udId = @id AND ud.udIsDeleted = 0";
            using var infoCmd = new SqlCommand(infoQ, conn, tran);
            infoCmd.Parameters.AddWithValue("@id", documentId);
            int userRoleId;
            string typeName;
            using (var r = await infoCmd.ExecuteReaderAsync())
            {
                if (!await r.ReadAsync())
                {
                    await tran.RollbackAsync();
                    return false;
                }
                userRoleId = r.GetInt32(0);
                typeName = r.GetString(1);
            }

            const string updateQ = @"UPDATE userDocumentation
                                      SET udName = @name, udFilePath = @fileData, udUploadedTimestamp = SYSUTCDATETIME(), udUploadedBy = @actor,
                                          udStatus = 'Pending', udVerifiedBy = NULL, udVerifiedAt = NULL, udRejectionReason = NULL
                                      WHERE udId = @id AND udIsDeleted = 0";
            using var updateCmd = new SqlCommand(updateQ, conn, tran);
            updateCmd.Parameters.AddWithValue("@name", fileName);
            updateCmd.Parameters.AddWithValue("@fileData", fileDataUrl);
            updateCmd.Parameters.AddWithValue("@actor", actorUserId);
            updateCmd.Parameters.AddWithValue("@id", documentId);
            int rows = await updateCmd.ExecuteNonQueryAsync();
            if (rows == 0)
            {
                await tran.RollbackAsync();
                return false;
            }

            await WriteAuditEntry(conn, tran, documentId, userRoleId, actorUserId, "Replaced", typeName, fileName);
            await tran.CommitAsync();
            return true;
        }
        catch
        {
            await tran.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Everything the caller needs to decide whether deleting a document would drop
    /// the profile below its minimum requirements - required document types may only
    /// ever be replaced, never bare-deleted, so this reports whether this document
    /// backs a required type at all.
    /// </summary>
    public virtual async Task<DocumentDeletionContext?> GetDeletionContext(int documentId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        const string q = @"SELECT ud.udUserRole, ud.udDtId, dt.dtName,
                                   CASE WHEN rdr.isRequired = 1 THEN 1 ELSE 0 END AS isRequired
                            FROM userDocumentation ud
                            JOIN documentType dt ON dt.dtId = ud.udDtId
                            JOIN userRoles ur ON ur.userRoleId = ud.udUserRole
                            JOIN roles ro ON ro.roleId = ur.urRoleId
                            LEFT JOIN roleDocumentRequirements rdr ON rdr.roleCode = ro.roleCode AND rdr.documentTypeId = ud.udDtId
                            WHERE ud.udId = @id AND ud.udIsDeleted = 0";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@id", documentId);
        using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;

        return new DocumentDeletionContext
        {
            UserRoleId = r.GetInt32(0),
            DocumentTypeId = r.GetInt32(1),
            DocumentTypeName = r.GetString(2),
            IsRequired = r.GetInt32(3) == 1
        };
    }

    /// <summary>Soft-deletes an (optional) document. Caller must have already confirmed via GetDeletionContext that it isn't a required document type.</summary>
    public virtual async Task<bool> DeleteDocument(int documentId, int actorUserId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var tran = (SqlTransaction)await conn.BeginTransactionAsync();

        try
        {
            const string infoQ = @"SELECT ud.udUserRole, dt.dtName, ud.udName
                                    FROM userDocumentation ud
                                    JOIN documentType dt ON dt.dtId = ud.udDtId
                                    WHERE ud.udId = @id AND ud.udIsDeleted = 0";
            using var infoCmd = new SqlCommand(infoQ, conn, tran);
            infoCmd.Parameters.AddWithValue("@id", documentId);
            int userRoleId;
            string typeName, fileName;
            using (var r = await infoCmd.ExecuteReaderAsync())
            {
                if (!await r.ReadAsync())
                {
                    await tran.RollbackAsync();
                    return false;
                }
                userRoleId = r.GetInt32(0);
                typeName = r.GetString(1);
                fileName = r.IsDBNull(2) ? "" : r.GetString(2);
            }

            const string deleteQ = "UPDATE userDocumentation SET udIsDeleted = 1 WHERE udId = @id AND udIsDeleted = 0";
            using var deleteCmd = new SqlCommand(deleteQ, conn, tran);
            deleteCmd.Parameters.AddWithValue("@id", documentId);
            int rows = await deleteCmd.ExecuteNonQueryAsync();
            if (rows == 0)
            {
                await tran.RollbackAsync();
                return false;
            }

            await WriteAuditEntry(conn, tran, documentId, userRoleId, actorUserId, "Deleted", typeName, fileName);
            await tran.CommitAsync();
            return true;
        }
        catch
        {
            await tran.RollbackAsync();
            throw;
        }
    }

    /// <summary>Full immutable audit trail for every document belonging to a role instance, newest first.</summary>
    public virtual async Task<List<DocumentAuditEntryDto>> GetAuditTrail(int userRoleId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        const string q = @"SELECT dal.logId, dal.actionType, dal.documentTypeName, dal.fileName,
                                   dal.actorUserId, ISNULL(u.uName + ' ' + u.uSurname, ''), dal.createdAt, dal.notes
                            FROM documentAuditLog dal
                            LEFT JOIN users u ON u.userId = dal.actorUserId
                            WHERE dal.userRoleId = @userRoleId
                            ORDER BY dal.createdAt DESC";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@userRoleId", userRoleId);
        using var r = await cmd.ExecuteReaderAsync();

        var list = new List<DocumentAuditEntryDto>();
        while (await r.ReadAsync())
        {
            list.Add(new DocumentAuditEntryDto
            {
                LogId = r.GetInt32(0),
                ActionType = r.GetString(1),
                DocumentTypeName = r.GetString(2),
                FileName = r.IsDBNull(3) ? null : r.GetString(3),
                ActorUserId = r.GetInt32(4),
                ActorName = r.GetString(5),
                CreatedAt = r.GetDateTime(6),
                Notes = r.IsDBNull(7) ? null : r.GetString(7)
            });
        }
        return list;
    }

    /// <summary>Admin queue: documents awaiting (or already given) a verification decision, oldest first.</summary>
    public virtual async Task<List<PendingDocumentVerificationDto>> GetPendingVerificationQueue(string? status = "Pending", string? roleCode = null)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        // roleDocumentRequirements is INNER-joined (not LEFT) deliberately: userDocumentation
        // is a shared table that HuntingPermit_db/Permit_db/Weapon_db also write to (SAPS
        // permits, firearm licences, their own 'Saved'/'Submitted' status vocab), and those
        // document types were never added to roleDocumentRequirements. Without this join
        // being INNER, every one of those unrelated rows would leak into this PJ/PH/VG/Farmer
        // admin queue and could be "Approved"/"Rejected" here, clobbering their own workflows.
        const string q = @"SELECT ud.udId, ud.udUserRole, u.userId, ISNULL(u.uName + ' ' + u.uSurname, ''), u.uEmail,
                                   ro.roleCode, ro.roleName, dt.dtId, dt.dtName, ud.udName, ud.udFilePath,
                                   ud.udUploadedTimestamp, rdr.isRequired,
                                   ud.udStatus, ud.udVerifiedAt, ud.udRejectionReason
                            FROM userDocumentation ud
                            JOIN documentType dt ON dt.dtId = ud.udDtId
                            JOIN userRoles ur ON ur.userRoleId = ud.udUserRole
                            JOIN roles ro ON ro.roleId = ur.urRoleId
                            JOIN users u ON u.userId = ur.urUserId
                            JOIN roleDocumentRequirements rdr ON rdr.roleCode = ro.roleCode AND rdr.documentTypeId = ud.udDtId
                            WHERE ud.udIsDeleted = 0
                              AND (@status IS NULL OR ud.udStatus = @status)
                              AND (@roleCode IS NULL OR ro.roleCode = @roleCode)
                            ORDER BY ud.udUploadedTimestamp ASC";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@status", (object?)status ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@roleCode", (object?)roleCode ?? DBNull.Value);
        using var r = await cmd.ExecuteReaderAsync();

        var list = new List<PendingDocumentVerificationDto>();
        while (await r.ReadAsync())
        {
            list.Add(new PendingDocumentVerificationDto
            {
                Id = r.GetInt32(0),
                UserRoleId = r.GetInt32(1),
                OwnerUserId = r.GetInt32(2),
                OwnerName = r.GetString(3),
                OwnerEmail = r.GetString(4),
                RoleCode = r.GetString(5),
                RoleName = r.GetString(6),
                DocumentTypeId = r.GetInt32(7),
                DocumentTypeName = r.GetString(8),
                FileName = r.IsDBNull(9) ? "" : r.GetString(9),
                FileDataUrl = r.IsDBNull(10) ? "" : r.GetString(10),
                UploadedAt = r.IsDBNull(11) ? DateTime.MinValue : r.GetDateTime(11),
                IsRequired = r.GetBoolean(12),
                Status = r.IsDBNull(13) ? "Pending" : r.GetString(13),
                VerifiedAt = r.IsDBNull(14) ? null : r.GetDateTime(14),
                RejectionReason = r.IsDBNull(15) ? null : r.GetString(15)
            });
        }
        return list;
    }

    /// <summary>
    /// Approves or rejects a document. The udStatus = 'Pending' guard doubles as
    /// optimistic concurrency - two admins can't both decide the same document, and
    /// a document already decided can't be re-decided through this path (returns
    /// null, same "already handled" contract as RoleVerifications_db.DecideAsync).
    /// </summary>
    public virtual async Task<DocumentVerificationResultDto?> VerifyDocument(int documentId, int adminUserId, bool approve, string? reason)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var tran = (SqlTransaction)await conn.BeginTransactionAsync();

        try
        {
            string newStatus = approve ? "Approved" : "Rejected";

            const string updateQ = @"
                UPDATE userDocumentation
                SET udStatus = @status, udVerifiedBy = @admin, udVerifiedAt = SYSUTCDATETIME(),
                    udRejectionReason = @reason
                OUTPUT inserted.udId, inserted.udUserRole, inserted.udName
                WHERE udId = @id AND udIsDeleted = 0 AND udStatus = 'Pending'";
            using var updateCmd = new SqlCommand(updateQ, conn, tran);
            updateCmd.Parameters.AddWithValue("@status", newStatus);
            updateCmd.Parameters.AddWithValue("@admin", adminUserId);
            updateCmd.Parameters.AddWithValue("@reason", approve ? (object)DBNull.Value : (object?)reason ?? DBNull.Value);
            updateCmd.Parameters.AddWithValue("@id", documentId);

            int userRoleId;
            string fileName;
            using (var r = await updateCmd.ExecuteReaderAsync())
            {
                if (!await r.ReadAsync())
                {
                    await tran.RollbackAsync();
                    return null;
                }
                userRoleId = r.GetInt32(1);
                fileName = r.IsDBNull(2) ? "" : r.GetString(2);
            }

            const string ownerQ = @"SELECT u.userId, ISNULL(u.uName + ' ' + u.uSurname, ''), u.uEmail, ro.roleCode, ro.roleName, dt.dtName
                                     FROM userDocumentation ud
                                     JOIN userRoles ur ON ur.userRoleId = ud.udUserRole
                                     JOIN roles ro ON ro.roleId = ur.urRoleId
                                     JOIN users u ON u.userId = ur.urUserId
                                     JOIN documentType dt ON dt.dtId = ud.udDtId
                                     WHERE ud.udId = @id";
            using var ownerCmd = new SqlCommand(ownerQ, conn, tran);
            ownerCmd.Parameters.AddWithValue("@id", documentId);

            int ownerUserId;
            string ownerName, ownerEmail, roleCode, roleName, documentTypeName;
            using (var r = await ownerCmd.ExecuteReaderAsync())
            {
                await r.ReadAsync();
                ownerUserId = r.GetInt32(0);
                ownerName = r.GetString(1);
                ownerEmail = r.GetString(2);
                roleCode = r.GetString(3);
                roleName = r.GetString(4);
                documentTypeName = r.GetString(5);
            }

            await WriteAuditEntry(conn, tran, documentId, userRoleId, adminUserId, newStatus, documentTypeName, fileName, reason);

            await tran.CommitAsync();
            return new DocumentVerificationResultDto
            {
                Id = documentId,
                Status = newStatus,
                OwnerUserId = ownerUserId,
                OwnerEmail = ownerEmail,
                OwnerName = ownerName,
                RoleCode = roleCode,
                RoleName = roleName,
                DocumentTypeName = documentTypeName,
                FileName = fileName,
                RejectionReason = approve ? null : reason
            };
        }
        catch
        {
            await tran.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Whether every required document type for the role behind userRoleId has an
    /// Approved, non-deleted document on file. Used to gate public discovery
    /// (People_db) and new bookings against a role's outstanding documents.
    /// </summary>
    public virtual async Task<bool> AreRequiredDocumentsApproved(int userRoleId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        // Fails closed for a userRoleId that doesn't exist (deleted/stale/invalid): such a
        // role must never read as "fully verified" just because it has no requirements to
        // fail - hence the explicit EXISTS(userRoles) check, not just NOT EXISTS(unmet ...).
        const string q = @"SELECT CASE WHEN EXISTS (SELECT 1 FROM userRoles WHERE userRoleId = @userRoleId)
                                    AND NOT EXISTS (
                                        SELECT 1
                                        FROM roleDocumentRequirements rdr
                                        JOIN userRoles ur ON ur.userRoleId = @userRoleId
                                        JOIN roles ro ON ro.roleId = ur.urRoleId
                                        WHERE rdr.roleCode = ro.roleCode AND rdr.isRequired = 1
                                          AND NOT EXISTS (
                                              SELECT 1 FROM userDocumentation ud
                                              WHERE ud.udUserRole = @userRoleId AND ud.udDtId = rdr.documentTypeId
                                                AND ud.udIsDeleted = 0 AND ud.udStatus = 'Approved'
                                          )
                                    ) THEN 1 ELSE 0 END";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@userRoleId", userRoleId);
        var result = await cmd.ExecuteScalarAsync();
        return result != null && Convert.ToInt32(result) == 1;
    }

    /// <summary>Resolves the real userId behind a userRoleId - for ownership checks.</summary>
    public virtual async Task<int> GetUserRoleOwnerUserId(int userRoleId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        const string q = "SELECT urUserId FROM userRoles WHERE userRoleId = @userRoleId";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@userRoleId", userRoleId);
        var result = await cmd.ExecuteScalarAsync();
        return result == null || result == DBNull.Value ? 0 : Convert.ToInt32(result);
    }

    /// <summary>Resolves which userRoleId a document belongs to, and that role's owning userId - for ownership checks on replace/delete.</summary>
    public virtual async Task<(int UserRoleId, int OwnerUserId)> GetDocumentOwner(int documentId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        const string q = @"SELECT ud.udUserRole, ur.urUserId
                            FROM userDocumentation ud
                            JOIN userRoles ur ON ur.userRoleId = ud.udUserRole
                            WHERE ud.udId = @id AND ud.udIsDeleted = 0";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@id", documentId);
        using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return (0, 0);
        return (r.GetInt32(0), r.GetInt32(1));
    }

    /// <summary>Inserts one documentAuditLog row within the caller's transaction - shared by upload/replace/delete/verify.</summary>
    private static async Task WriteAuditEntry(SqlConnection conn, SqlTransaction tran, int documentId, int userRoleId, int actorUserId, string actionType, string documentTypeName, string? fileName, string? notes = null)
    {
        const string q = @"INSERT INTO documentAuditLog (documentId, userRoleId, actorUserId, actionType, documentTypeName, fileName, notes)
                            VALUES (@documentId, @userRoleId, @actorUserId, @actionType, @typeName, @fileName, @notes)";
        using var cmd = new SqlCommand(q, conn, tran);
        cmd.Parameters.AddWithValue("@documentId", documentId);
        cmd.Parameters.AddWithValue("@userRoleId", userRoleId);
        cmd.Parameters.AddWithValue("@actorUserId", actorUserId);
        cmd.Parameters.AddWithValue("@actionType", actionType);
        cmd.Parameters.AddWithValue("@typeName", documentTypeName);
        cmd.Parameters.AddWithValue("@fileName", (object?)fileName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Whether documentTypeId is one of the types roleDocumentRequirements actually
    /// defines for the role behind userRoleId - blocks uploading an arbitrary/unrelated
    /// document type (including nonexistent ids, which would otherwise fail an FK
    /// constraint deep in UploadDocument and leak a raw SQL error to the caller).</summary>
    public virtual async Task<bool> IsValidDocumentTypeForRole(int userRoleId, int documentTypeId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        const string q = @"SELECT COUNT(1)
                            FROM userRoles ur
                            JOIN roles ro ON ro.roleId = ur.urRoleId
                            JOIN roleDocumentRequirements rdr ON rdr.roleCode = ro.roleCode AND rdr.documentTypeId = @dtId
                            WHERE ur.userRoleId = @userRoleId";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@userRoleId", userRoleId);
        cmd.Parameters.AddWithValue("@dtId", documentTypeId);
        var count = (int)(await cmd.ExecuteScalarAsync())!;
        return count > 0;
    }

    /// <summary>Resolves the real roleCode behind a userRoleId - used to reject a public
    /// summary request whose roleCode query param doesn't actually match the role.</summary>
    public virtual async Task<string?> GetRoleCodeForUserRole(int userRoleId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        const string q = @"SELECT ro.roleCode FROM userRoles ur JOIN roles ro ON ro.roleId = ur.urRoleId WHERE ur.userRoleId = @userRoleId";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@userRoleId", userRoleId);
        var result = await cmd.ExecuteScalarAsync();
        return result as string;
    }
}

/// <summary>What DeleteDocument needs to decide whether a deletion is allowed (a required document type can never be bare-deleted).</summary>
public class DocumentDeletionContext
{
    public int UserRoleId { get; set; }
    public int DocumentTypeId { get; set; }
    public string DocumentTypeName { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
}
