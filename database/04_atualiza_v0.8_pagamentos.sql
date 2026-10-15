/* =====================================================================
   Atualização v0.7 -> v0.8: controle de pagamentos dos clientes
   Rode UMA vez em banco criado com o 01_schema.sql v0.7 (ou v0.6 já atualizado pelo 03).
   Banco novo: use direto o 01_schema.sql v0.8.
   ===================================================================== */
CREATE SCHEMA financeiro;
GO

ALTER TABLE seguranca.Clinica ADD
    ValorMensalidade   DECIMAL(10,2) NULL,
    DiaVencimento      TINYINT  NOT NULL CONSTRAINT DF_Clinica_DiaVencimento DEFAULT (10),
    DiasTolerancia     SMALLINT NOT NULL CONSTRAINT DF_Clinica_DiasTolerancia DEFAULT (5),
    BloqueioPorAtraso  BIT      NOT NULL CONSTRAINT DF_Clinica_BloqueioPorAtraso DEFAULT (1),
    BloqueadoPorAtraso BIT      NOT NULL CONSTRAINT DF_Clinica_BloqueadoPorAtraso DEFAULT (0);
GO
ALTER TABLE seguranca.Clinica ADD CONSTRAINT CK_Clinica_Cobranca CHECK ((ValorMensalidade IS NULL OR ValorMensalidade > 0)
    AND DiaVencimento BETWEEN 1 AND 28 AND DiasTolerancia BETWEEN 0 AND 60);
GO

-- Cobranças dos clientes (mensalidades e avulsas) e o pagamento de cada uma. Mantidas só pela área /Admin.
-- Cobrança sem pagamento depois de Vencimento + Clinica.DiasTolerancia bloqueia o cliente (se Clinica.BloqueioPorAtraso = 1).
CREATE TABLE financeiro.Cobranca (
    Id             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Cobranca PRIMARY KEY,
    ClinicaId      INT            NOT NULL CONSTRAINT FK_Cobranca_Clinica REFERENCES seguranca.Clinica(Id),
    Descricao      NVARCHAR(100)  NOT NULL,
    Competencia    DATE           NULL,          -- 1º dia do mês da mensalidade; NULL = cobrança avulsa
    Vencimento     DATE           NOT NULL,
    Valor          DECIMAL(10,2)  NOT NULL,
    PagoEm         DATE           NULL,
    ValorPago      DECIMAL(10,2)  NULL,
    FormaPagamento VARCHAR(15)    NULL,          -- PIX, BOLETO, CARTAO, TRANSFERENCIA, DINHEIRO, OUTRO
    Observacoes    NVARCHAR(300)  NULL,
    Cancelada      BIT            NOT NULL CONSTRAINT DF_Cobranca_Cancelada DEFAULT (0),
    CriadoEm       DATETIME2(0)   NOT NULL CONSTRAINT DF_Cobranca_CriadoEm DEFAULT (SYSDATETIME()),
    AtualizadoEm   DATETIME2(0)   NULL,
    CONSTRAINT CK_Cobranca_Valor CHECK (Valor > 0 AND (ValorPago IS NULL OR ValorPago > 0)),
    CONSTRAINT CK_Cobranca_Pagamento CHECK ((PagoEm IS NULL AND ValorPago IS NULL AND FormaPagamento IS NULL)
                                            OR (PagoEm IS NOT NULL AND ValorPago IS NOT NULL AND FormaPagamento IS NOT NULL)),
    CONSTRAINT CK_Cobranca_Forma CHECK (FormaPagamento IS NULL OR FormaPagamento IN ('PIX','BOLETO','CARTAO','TRANSFERENCIA','DINHEIRO','OUTRO')),
    CONSTRAINT CK_Cobranca_Competencia CHECK (Competencia IS NULL OR DAY(Competencia) = 1)
);
-- Uma mensalidade válida por mês e cliente (a rotina automática também não recria um mês cancelado)
CREATE UNIQUE INDEX UX_Cobranca_Competencia ON financeiro.Cobranca (ClinicaId, Competencia) WHERE Competencia IS NOT NULL AND Cancelada = 0;
CREATE INDEX IX_Cobranca_Pendentes ON financeiro.Cobranca (ClinicaId, Vencimento) WHERE PagoEm IS NULL AND Cancelada = 0;
CREATE INDEX IX_Cobranca_Vencimento ON financeiro.Cobranca (Vencimento);
GO
