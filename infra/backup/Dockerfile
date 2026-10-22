# pg_dump da mesma versão do banco, mais o curl para mandar a cópia pelo Resend.
FROM postgres:17
RUN apt-get update && apt-get install -y --no-install-recommends curl ca-certificates && rm -rf /var/lib/apt/lists/*
COPY infra/backup/backup.sh /backup.sh
ENTRYPOINT ["/bin/sh", "/backup.sh"]
