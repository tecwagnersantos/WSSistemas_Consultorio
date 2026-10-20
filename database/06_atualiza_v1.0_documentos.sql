/* =====================================================================
   Atualização v0.9 -> v1.0: documentos do prontuário (atestado, declaração de comparecimento,
   solicitação de exames, encaminhamento) e modelos de texto por clínica.
   Rode UMA vez em banco criado com o 01_schema.sql v0.9 (ou já atualizado pelo 05).
   Banco novo: use direto o 01_schema.sql v1.0.
   ===================================================================== */
SET XACT_ABORT ON;
BEGIN TRANSACTION;

CREATE TABLE prontuario.Documento (
    Id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Documento PRIMARY KEY,
    ClinicaId         INT            NOT NULL,
    PacienteId        INT            NOT NULL,
    MedicoId          INT            NOT NULL,
    EvolucaoId        INT            NULL,
    Tipo              VARCHAR(20)    NOT NULL,     -- ATESTADO, COMPARECIMENTO, EXAMES, ENCAMINHAMENTO
    Titulo            NVARCHAR(100)  NOT NULL,
    Texto             NVARCHAR(MAX)  NOT NULL,
    DiasAfastamento   SMALLINT       NULL,
    Cid               VARCHAR(10)    NULL,         -- só com autorização do paciente (Res. CFM 1.658/2002)
    EmitidoEm         DATETIME2(0)   NOT NULL CONSTRAINT DF_Documento_EmitidoEm DEFAULT (SYSDATETIME()),
    CONSTRAINT FK_Documento_Paciente FOREIGN KEY (ClinicaId, PacienteId) REFERENCES cadastro.Paciente (ClinicaId, Id),
    CONSTRAINT FK_Documento_Medico   FOREIGN KEY (ClinicaId, MedicoId)   REFERENCES cadastro.Medico (ClinicaId, Id),
    CONSTRAINT FK_Documento_Evolucao FOREIGN KEY (ClinicaId, EvolucaoId) REFERENCES prontuario.Evolucao (ClinicaId, Id),
    CONSTRAINT CK_Documento_Tipo CHECK (Tipo IN ('ATESTADO','COMPARECIMENTO','EXAMES','ENCAMINHAMENTO')),
    CONSTRAINT CK_Documento_Dias CHECK (DiasAfastamento IS NULL OR DiasAfastamento BETWEEN 1 AND 365)
);
CREATE INDEX IX_Documento_Paciente ON prontuario.Documento (ClinicaId, PacienteId, EmitidoEm DESC);

CREATE TABLE prontuario.ModeloDocumento (
    Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ModeloDocumento PRIMARY KEY,
    ClinicaId    INT            NOT NULL CONSTRAINT FK_ModeloDocumento_Clinica REFERENCES seguranca.Clinica(Id),
    Tipo         VARCHAR(20)    NOT NULL,
    Nome         NVARCHAR(100)  NOT NULL,
    Texto        NVARCHAR(MAX)  NOT NULL,
    Ativo        BIT            NOT NULL CONSTRAINT DF_ModeloDocumento_Ativo DEFAULT (1),
    CriadoEm     DATETIME2(0)   NOT NULL CONSTRAINT DF_ModeloDocumento_CriadoEm DEFAULT (SYSDATETIME()),
    AtualizadoEm DATETIME2(0)   NULL,
    CONSTRAINT CK_ModeloDocumento_Tipo CHECK (Tipo IN ('ATESTADO','COMPARECIMENTO','EXAMES','ENCAMINHAMENTO'))
);
CREATE INDEX IX_ModeloDocumento_Clinica ON prontuario.ModeloDocumento (ClinicaId, Tipo, Nome);

COMMIT;
GO
