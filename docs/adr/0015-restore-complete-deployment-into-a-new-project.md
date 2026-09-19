# 0015 — Restore a complete deployment into a new project

Status: accepted

The supported backup unit is a consistent custom-format `pg_dump` of the
whole `upaffe` database plus the deployment `.env`, Compose files, scripts,
and mounted secret files. PostgreSQL is the only application state volume.
The database includes operator access, project and monitor configuration,
saved credentials, incident and history records, deadlines, and pending
notifications. A table-selective dump or a copy of the running data directory
would not provide this deployment contract. The backup directory is private
and portable between hosts; its database archive has a SHA-256 checksum.

Restoration requires a new deployment directory and an unused Compose project
with a new PostgreSQL volume. The helper rejects existing destinations and
project resources. It restores the archive into the new database in one
transaction before starting the application. The saved PostgreSQL major
version and application image revision are used first, so forward migrations
cannot silently change the restored copy. A temporary verification overlay
stops monitoring, email delivery, and retention while an operator checks the
copy. Cutover requires stopping the old application, adapting external HTTPS
and network settings, and then starting the restored application normally.

The backup includes confidential database contents and receiver credentials.
The operator protects and transfers it outside upaffe, retains external TLS
material and issued client tokens separately, and chooses a backup schedule.
The application does not run backup jobs. See the [operations guide](../operations.md#back-up-a-production-installation)
for commands and failure handling.
