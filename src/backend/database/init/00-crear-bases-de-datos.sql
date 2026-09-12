-- Cada microservicio posee su propia base de datos (database-per-service,
-- ver docs/01-arquitectura/arquitectura-general.md). Las tablas dentro de
-- cada una las crea EF Core Migrations al arrancar el servicio
-- correspondiente (Database.Migrate() en Program.cs) — este script solo
-- provisiona los contenedores de base de datos vacíos.
--
-- El archivo ../schema.sql es el diseño de datos de REFERENCIA para una
-- implementación SQL nativa (con procedimientos almacenados para el
-- hash-chain, ver stored-procedures.sql) y NO se ejecuta automáticamente:
-- la implementación que realmente corre usa EF Core Code-First, con el
-- hash-chain calculado en C# (ver SecureSign.Evidence.Domain.EventoEvidencia).

CREATE DATABASE securesign_documents;
CREATE DATABASE securesign_signature;
CREATE DATABASE securesign_evidence;
CREATE DATABASE securesign_identity;
