# ops/backup — database backup, restore & verification

Closes the "no backup/restore automation" production gap. Three scripts, all
env-var driven (`DB_HOST`/`DB_PORT`/`DB_USER`/`DB_PASS`, same convention as
`db/build_from_scratch.sh`), never hardcode credentials:

| Script | What it does |
|---|---|
| `backup.sh` | `pg_dump` custom-format archive + globals (roles), integrity-checks it, prunes past `BACKUP_RETENTION_DAYS` (14), optional offsite upload (`BACKUP_S3_URI` via aws cli, or `RCLONE_REMOTE` via rclone) |
| `restore.sh <dump> <target-db> [--drop-existing]` | Restores into a **new** DB by default; replacing an existing DB requires `--drop-existing` **and** typing the DB name back (`RESTORE_FORCE=1` for scripted DR) |
| `verify-backup.sh [dump]` | Restores the newest backup into a throwaway `postgres:18` Docker container and sanity-checks table/schema counts — run weekly; a backup that has never been restored is a hope, not a backup |

## Scheduling

**macOS (current dev/host box):** launchd agent, daily 02:00 (before partman
maintenance at 02:30):

```bash
cp ops/backup/com.laundryghar.backup.plist ~/Library/LaunchAgents/
launchctl load -w ~/Library/LaunchAgents/com.laundryghar.backup.plist
launchctl start com.laundryghar.backup   # test-fire once
```

**Linux server:** crontab (`crontab -e`):

```cron
0 2 * * *  DB_HOST=db.internal DB_USER=postgres DB_PASS=... BACKUP_S3_URI=s3://laundryghar-backups/db  /opt/laundryghar/ops/backup/backup.sh >> /var/log/laundryghar-backup.log 2>&1
0 4 * * 0  DB_NAME=laundry_ghar_db  /opt/laundryghar/ops/backup/verify-backup.sh >> /var/log/laundryghar-verify.log 2>&1
```

**Managed PostgreSQL (RDS / Cloud SQL / Neon):** keep the provider's automated
snapshots as the primary mechanism and run `backup.sh` as the portable,
provider-independent second copy (snapshots don't leave the provider).

## Notes

- `pg_dump` must be able to read every schema — use `postgres`/`app_admin`,
  **not** the RLS-scoped `app_user` (it would silently back up only its rows).
- Client major version must match the server: on macOS
  `export PATH="/opt/homebrew/opt/postgresql@18/bin:$PATH"`.
- Custom format (`-Fc`) supports parallel & selective restore
  (`pg_restore -j4`, `--table=...`), unlike plain SQL dumps.
- Roles are cluster-level: on a brand-new cluster apply the paired
  `globals_*.sql` **before** `restore.sh` so GRANTs/RLS policies bind.
- Disaster-recovery runbook:
  1. `psql -f globals_<stamp>.sql` (new cluster only)
  2. `ops/backup/restore.sh <dump> laundry_ghar_db --drop-existing`
  3. `db/tools/migrate.sh status` — confirm schema version matches the app
  4. restart backend services (EF model ↔ schema must agree)
