"""Remove what the E2E runs created, without touching the append-only audit trail.

Every test account is <role>-<run>@staging-e2e.test. Rows are deleted children-first along the
real foreign-key graph. Rows that the append-only tables (ratingHistory, documentAuditLog,
adminAuditLog - they refuse DELETE by trigger) still point at can't go, and neither can their
parents; those test accounts are suspended and their packages/farms set Inactive instead, so
nothing shows up on the site.

  python cleanup.py              do it (run_all.py calls this after every run)
  python cleanup.py --dry-run    only print the plan
"""
import sys
from collections import defaultdict

from st import sql

PRE = "SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET XACT_ABORT ON; "
# Rows here are kept on purpose: the audit trail, and accounts/roles that audit rows may name.
KEEP = {"users", "userroles", "userdocumentation", "documentauditlog", "ratinghistory", "adminauditlog"}


def ints(text):
    return {int(x) for x in text.split() if x.strip().lstrip("-").isdigit()}


def in_list(values):
    return ",".join(map(str, sorted(values))) or "0"


children = defaultdict(list)        # referenced table -> [(child table, child col, referenced col)]
for line in sql("""SELECT OBJECT_NAME(fk.parent_object_id), cp.name, OBJECT_NAME(fk.referenced_object_id), cr.name
                   FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
                   JOIN sys.columns cp ON cp.object_id = fkc.parent_object_id AND cp.column_id = fkc.parent_column_id
                   JOIN sys.columns cr ON cr.object_id = fkc.referenced_object_id AND cr.column_id = fkc.referenced_column_id""").splitlines():
    child, ccol, parent, pcol = line.split("|")
    children[parent.lower()].append((child, ccol, pcol))
pk = {}
for line in sql("""SELECT t.name, c.name FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
                   JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id JOIN sys.tables t ON t.object_id=i.object_id
                   WHERE i.is_primary_key=1""").splitlines():
    t, c = line.split("|")
    pk.setdefault(t.lower(), c)

plan = defaultdict(set)            # table -> primary keys to delete
order = []                          # children before parents


def collect(table, keys):
    new = set(keys) - plan[table]
    if not new:
        return
    plan[table] |= new
    for child, ccol, pcol in children[table.lower()]:
        if child.lower() not in pk:
            continue
        ref = new if pcol.lower() == pk[table.lower()].lower() else \
            ints(sql(f"SELECT [{pcol}] FROM dbo.[{table}] WHERE {pk[table.lower()]} IN ({in_list(new)})"))
        if ref:
            collect(child, ints(sql(f"SELECT {pk[child.lower()]} FROM dbo.[{child}] WHERE [{ccol}] IN ({in_list(ref)})")))
    order.append(table)


users = ints(sql("SELECT userId FROM users WHERE uEmail LIKE '%@staging-e2e.test'"))
print(f"test accounts: {len(users)}")
if not users:
    sys.exit(0)
collect("users", users)
chats = ints(sql(f"SELECT DISTINCT cpChatId FROM chatParticipants WHERE cpUserId IN ({in_list(users)})"))
shared = ints(sql(f"SELECT DISTINCT cpChatId FROM chatParticipants WHERE cpChatId IN ({in_list(chats)}) AND cpUserId NOT IN ({in_list(users)})"))
collect("chat", chats - shared)

final = []
for t in order:
    if t.lower() not in KEEP and t not in final and plan[t]:
        final.append(t)
for t in final:
    print(f"  {t:32} {len(plan[t]):4} rows")
if "--dry-run" in sys.argv:
    sys.exit(0)

deleted = []
for _ in range(3):      # a later pass can succeed once a child went in an earlier one
    for t in final:
        if t in deleted:
            continue
        if sql(PRE + f"DELETE FROM dbo.[{t}] WHERE {pk[t.lower()]} IN ({in_list(plan[t])}); SELECT 'ok';").strip().endswith("ok"):
            deleted.append(t)
kept = [t for t in final if t not in deleted]
sql(PRE + f"""UPDATE dbo.users SET uIsSuspended = 1, uAccountStatus = 'Suspended', uStatusReason = N'E2E-toetsrekening' WHERE userId IN ({in_list(users)});
UPDATE dbo.packages SET pStatus = 'Inactive' WHERE packageId IN ({in_list(plan['packages'])});
UPDATE dbo.farm SET farmStatus = 'Inactive' WHERE farmId IN ({in_list(plan['farm'])});""")
print(f"cleanup: deleted from {len(deleted)} tables; kept (held by the audit trail): {kept or 'none'}; "
      f"{len(users)} test accounts suspended")
