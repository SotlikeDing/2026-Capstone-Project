"""Generate docs/database/er-diagram.md (Mermaid) from a live Mikpunt database.

  set SQLCMDPASSWORD=...        (never on the command line)
  python tools/python/generate_er_diagram.py --server "10.0.0.41,1433" --user <login> [--database Mikpunt]

Shows every table with its primary-key and foreign-key columns, every foreign key as a
relationship, and a column/row summary table. Needs sqlcmd on PATH.
"""
import argparse, collections, datetime, os, subprocess, sys

parser = argparse.ArgumentParser()
parser.add_argument("--server", required=True)
parser.add_argument("--user", required=True)
parser.add_argument("--database", default="Mikpunt")
parser.add_argument("--out", default=os.path.join(os.path.dirname(__file__), "..", "..", "docs", "database", "er-diagram.md"))
args = parser.parse_args()
if not os.environ.get("SQLCMDPASSWORD"):
    sys.exit("Set SQLCMDPASSWORD in the environment.")


def query(sql):
    out = subprocess.run(["sqlcmd", "-S", args.server, "-U", args.user, "-d", args.database, "-N", "-C", "-W", "-h", "-1",
                          "-s", "|", "-Q", "SET NOCOUNT ON; " + sql], capture_output=True, text=True, check=True)
    return [line.split("|") for line in out.stdout.strip().splitlines() if line.strip()]


columns = query("""SELECT t.name, c.name, TYPE_NAME(c.user_type_id), c.is_nullable, c.column_id
                   FROM sys.tables t JOIN sys.columns c ON c.object_id = t.object_id
                   WHERE t.is_ms_shipped = 0 ORDER BY t.name, c.column_id""")
pks = {(t, c) for t, c in query("""SELECT t.name, c.name FROM sys.indexes i
                   JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                   JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                   JOIN sys.tables t ON t.object_id = i.object_id WHERE i.is_primary_key = 1""")}
fks = query("""SELECT OBJECT_NAME(f.parent_object_id), COL_NAME(fc.parent_object_id, fc.parent_column_id),
                      OBJECT_NAME(f.referenced_object_id), COL_NAME(fc.referenced_object_id, fc.referenced_column_id),
                      COLUMNPROPERTY(fc.parent_object_id, COL_NAME(fc.parent_object_id, fc.parent_column_id), 'AllowsNull')
               FROM sys.foreign_keys f JOIN sys.foreign_key_columns fc ON fc.constraint_object_id = f.object_id
               ORDER BY 1, 2""")
rows = dict(query("""SELECT t.name, SUM(p.rows) FROM sys.tables t JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
                     GROUP BY t.name"""))
triggers = collections.defaultdict(list)
for t, name in query("SELECT OBJECT_NAME(parent_id), name FROM sys.triggers WHERE parent_class = 1"):
    triggers[t].append(name)

fk_cols = {(t, c) for t, c, *_ in fks}
by_table = collections.OrderedDict()
for t, c, ty, nullable, _ in columns:
    by_table.setdefault(t, []).append((c, ty, nullable == "1"))

lines = ["# Database: entity-relationship diagram", "",
         f"Generated from the live `{args.database}` schema on {datetime.date.today():%Y-%m-%d} by "
         "`tools/python/generate_er_diagram.py` - regenerate after schema changes rather than editing by hand.", "",
         f"{len(by_table)} tables, {len(fks)} foreign keys. Columns shown: primary keys (PK) and foreign keys (FK); "
         "the full column list per table is in the summary below.", "",
         "```mermaid", "erDiagram"]
for t, cols in by_table.items():
    keyed = [(c, ty) for c, ty, _ in cols if (t, c) in pks or (t, c) in fk_cols]
    lines.append(f"    {t} {{")
    for c, ty in keyed:
        tag = "PK" if (t, c) in pks else "FK"
        lines.append(f"        {ty.replace(' ', '_')} {c} {tag}")
    lines.append("    }")
for child, ccol, parent, pcol, nullable in fks:
    card = "|o--o{" if nullable == "1" else "||--o{"
    lines.append(f'    {parent} {card} {child} : "{ccol}"')
lines += ["```", "", "## Tables", "", "| Table | Columns | Rows | Notes |", "|---|---|---|---|"]
for t, cols in by_table.items():
    notes = ", ".join(f"`{n}`" for n in triggers.get(t, []))
    if notes:
        notes = "append-only: " + notes
    lines.append(f"| `{t}` | {len(cols)} | {rows.get(t, '?')} | {notes} |")

os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
with open(args.out, "w", encoding="utf-8", newline="\n") as f:
    f.write("\n".join(lines) + "\n")
print(f"wrote {args.out}: {len(by_table)} tables, {len(fks)} relationships")
