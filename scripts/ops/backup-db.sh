#!/bin/sh
# Nightly full backup of the Mikpunt database, run on the docker host by /etc/cron.d/mikpunt-backup:
#   15 2 * * * root /opt/project/scripts/backup-db.sh >> /var/log/mikpunt-backup.log 2>&1
#
# Uses the app's own connection string from /opt/project/.env (no second copy of the password)
# and Microsoft's sqlcmd image, so nothing has to be installed on the host. SQL Server writes the
# file on its own disk: /var/opt/mssql/data/backups/<db>_<weekday>.bak - seven rolling daily
# backups, each overwritten a week later. Restore steps: docs/ops.md.
set -eu

ENV_FILE="${ENV_FILE:-/opt/project/.env}"
BACKUP_DIR="${BACKUP_DIR:-/var/opt/mssql/data/backups}"
IMAGE="mcr.microsoft.com/mssql-tools:latest"

CS=$(grep -E '^ConnectionStrings__DefaultConnection=' "$ENV_FILE" | head -1 | cut -d= -f2- | sed -e 's/^"//' -e 's/"$//')
part() { echo "$CS" | tr ';' '\n' | grep -iE "^[[:space:]]*($1)[[:space:]]*=" | head -1 | cut -d= -f2-; }
SERVER=$(part 'Server|Data Source')
DB=$(part 'Database|Initial Catalog')
LOGIN=$(part 'User Id|User ID|UID|User')
SQLCMDPASSWORD=$(part 'Password|PWD')
export SQLCMDPASSWORD
[ -n "$SERVER" ] && [ -n "$DB" ] && [ -n "$LOGIN" ] && [ -n "$SQLCMDPASSWORD" ] || { echo "$(date -Is) cannot parse the connection string in $ENV_FILE"; exit 1; }

FILE="$BACKUP_DIR/${DB}_$(date +%a).bak"
START=$(date +%s)
docker run --rm -e SQLCMDPASSWORD "$IMAGE" /opt/mssql-tools/bin/sqlcmd \
    -S "$SERVER" -U "$LOGIN" -d "$DB" -N -C -b -l 30 \
    -Q "BACKUP DATABASE [$DB] TO DISK = N'$FILE' WITH INIT, FORMAT, COMPRESSION, CHECKSUM, NAME = N'$DB nightly $(date -I)'"
echo "$(date -Is) backup of $DB to $FILE OK in $(( $(date +%s) - START ))s"
