/* =====================================================================
   Sistema de Consultório Médico - Modelo de dados (v1.0 - multi-clínica)
   SQL Server 2019+ | Script para banco NOVO - não executado em SQL Server real ainda
   Premissas (decisões de arquitetura): banco único compartilhado por vários clientes
   (clínicas), com ClinicaId em todas as tabelas de dados; perfis de acesso configuráveis
   por transação (padrões Administrador/Médico/Recepção); administradores do sistema em tabela
   própria (área /Admin, login separado); prontuário liberado só ao perfil Médico por padrão;
   EF Core; hospedagem própria em IIS.
   ---------------------------------------------------------------------
   Schemas:
     seguranca  - usuários, perfis, permissões, auditoria
     cadastro   - especialidades, médicos, convênios, planos, pacientes
     agenda     - horários de atendimento, bloqueios, agendamentos
     prontuario - resumo clínico, evoluções, diagnósticos, prescrições, anexos
     financeiro - cobranças e pagamentos dos clientes (só a área /Admin)

   Convenções:
     - Nomes em português, PascalCase (mapeia direto para entidades EF Core).
     - PK "Id" INT IDENTITY (BIGINT em tabelas de alto volume).
     - Datas em DATETIME2 no horário local do consultório (America/Sao_Paulo).
     - Exclusão lógica via coluna "Ativo" nos cadastros; prontuário nunca é apagado.
     - Colunas de auditoria: CriadoEm / AtualizadoEm.

   Isolamento por cliente (multi-clínica):
     - seguranca.Clinica é o cliente. Toda tabela de dados tem ClinicaId NOT NULL.
     - Cada tabela de dados tem UNIQUE (ClinicaId, Id) e as chaves estrangeiras entre
       elas são compostas (ClinicaId, XxxId): o banco recusa, por exemplo, um
       agendamento da clínica A apontando para um paciente da clínica B.
     - Unicidades de negócio são por clínica (CPF do paciente, CRM do médico, nomes).
     - A aplicação filtra tudo pela clínica do usuário logado (filtro global do EF Core
       e WHERE ClinicaId = @clinicaId nos relatórios Dapper).
     - Situação do cliente (ATIVO/BLOQUEADO/INATIVO): só ATIVO entra no sistema.
     - Período de acesso: enquanto AcessoDefinitivo = 0 a clínica só entra até AcessoAte
       (teste/implantação). Liberada a versão definitiva, AcessoAte deixa de valer.
     - Todo usuário pertence a uma clínica; o e-mail (login) é único no sistema inteiro.
     - seguranca.AdministradorSistema: equipe do sistema (área /Admin), com login e
       cookie separados. Mantém clientes, limites (médicos/usuários) e vê relatórios
       consolidados; não acessa agenda nem prontuário.
   ===================================================================== */

-- Sugestão de criação do banco (collation sem acento/caixa para buscas por nome)
-- CREATE DATABASE Consultorio COLLATE Latin1_General_100_CI_AI_SC_UTF8;
-- GO
-- USE Consultorio;
-- GO

CREATE SCHEMA seguranca;
GO
CREATE SCHEMA cadastro;
GO
CREATE SCHEMA agenda;
GO
CREATE SCHEMA prontuario;
GO
CREATE SCHEMA financeiro;
GO

/* =====================================================================
   SEGURANÇA
   ===================================================================== */

-- Clientes do sistema (consultórios/clínicas)
CREATE TABLE seguranca.Clinica (
    Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Clinica PRIMARY KEY,
    Nome          NVARCHAR(150) NOT NULL,
    Cnpj          CHAR(14)      NULL,
    Telefone      VARCHAR(20)   NULL,
    Email         VARCHAR(254)  NULL,
    Endereco      NVARCHAR(200) NULL,
    Cidade        NVARCHAR(80)  NULL,
    Uf            CHAR(2)       NULL,
    MaxMedicos    SMALLINT      NULL,          -- limite de médicos ATIVOS do plano (NULL = sem limite)
    MaxUsuarios   SMALLINT      NULL,          -- limite de usuários ATIVOS do plano (NULL = sem limite)
    Observacoes   NVARCHAR(500) NULL,          -- anotações internas do suporte
    AcessoDefinitivo BIT        NOT NULL CONSTRAINT DF_Clinica_AcessoDefinitivo DEFAULT (0), -- 0 = período de teste/implantação
    AcessoAte     DATE          NULL,          -- último dia de acesso enquanto não for definitivo
    -- Cobrança: mensalidade (NULL = não gera), dia do vencimento, tolerância e bloqueio automático por atraso
    ValorMensalidade DECIMAL(10,2) NULL,
    DiaVencimento TINYINT       NOT NULL CONSTRAINT DF_Clinica_DiaVencimento DEFAULT (10),
    DiasTolerancia SMALLINT     NOT NULL CONSTRAINT DF_Clinica_DiasTolerancia DEFAULT (5),
    BloqueioPorAtraso BIT       NOT NULL CONSTRAINT DF_Clinica_BloqueioPorAtraso DEFAULT (1),
    BloqueadoPorAtraso BIT      NOT NULL CONSTRAINT DF_Clinica_BloqueadoPorAtraso DEFAULT (0), -- 1 = bloqueio atual foi automático (pagamento reativa)
    -- Situação do cliente: ATIVO (usa o sistema), BLOQUEADO (suspensão temporária, ex.: inadimplência),
    -- INATIVO (deixou de ser cliente). Só ATIVO entra; os dados nunca são apagados.
    Situacao      VARCHAR(10)   NOT NULL CONSTRAINT DF_Clinica_Situacao DEFAULT ('ATIVO'),
    MotivoSituacao NVARCHAR(300) NULL,         -- obrigatório para BLOQUEADO/INATIVO (validado na aplicação)
    SituacaoDesde DATETIME2(0)  NOT NULL CONSTRAINT DF_Clinica_SituacaoDesde DEFAULT (SYSDATETIME()),
    CriadoEm      DATETIME2(0)  NOT NULL CONSTRAINT DF_Clinica_CriadoEm DEFAULT (SYSDATETIME()),
    AtualizadoEm  DATETIME2(0)  NULL,
    CONSTRAINT CK_Clinica_Limites CHECK ((MaxMedicos IS NULL OR MaxMedicos >= 0) AND (MaxUsuarios IS NULL OR MaxUsuarios >= 0)),
    CONSTRAINT CK_Clinica_Acesso CHECK (AcessoDefinitivo = 1 OR AcessoAte IS NOT NULL),
    CONSTRAINT CK_Clinica_Situacao CHECK (Situacao IN ('ATIVO', 'BLOQUEADO', 'INATIVO')),
    CONSTRAINT CK_Clinica_Cobranca CHECK ((ValorMensalidade IS NULL OR ValorMensalidade > 0) AND DiaVencimento BETWEEN 1 AND 28
                                          AND DiasTolerancia BETWEEN 0 AND 60)
);
CREATE UNIQUE INDEX UX_Clinica_Cnpj ON seguranca.Clinica (Cnpj) WHERE Cnpj IS NOT NULL;

-- Perfil de acesso = conjunto de transações (PerfilPermissao). ClinicaId NULL: perfil padrão, disponível para todos os clientes;
-- preenchido: perfil exclusivo daquele cliente. Mantidos pelo administrador do sistema em /Admin → Perfis de acesso.
CREATE TABLE seguranca.Perfil (
    Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Perfil PRIMARY KEY,
    ClinicaId    INT           NULL CONSTRAINT FK_Perfil_Clinica REFERENCES seguranca.Clinica(Id),
    Nome         NVARCHAR(50)  NOT NULL,
    Descricao    NVARCHAR(200) NULL,
    Ativo        BIT           NOT NULL CONSTRAINT DF_Perfil_Ativo DEFAULT (1),
    CriadoEm     DATETIME2(0)  NOT NULL CONSTRAINT DF_Perfil_CriadoEm DEFAULT (SYSDATETIME()),
    AtualizadoEm DATETIME2(0)  NULL
);
CREATE UNIQUE INDEX UX_Perfil_Nome_Padrao  ON seguranca.Perfil (Nome) WHERE ClinicaId IS NULL;
CREATE UNIQUE INDEX UX_Perfil_Nome_Clinica ON seguranca.Perfil (ClinicaId, Nome) WHERE ClinicaId IS NOT NULL;

CREATE TABLE seguranca.Permissao (
    Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Permissao PRIMARY KEY,
    Codigo      VARCHAR(80)   NOT NULL CONSTRAINT UQ_Permissao_Codigo UNIQUE, -- transação, ex.: 'agenda.agendar'
    Descricao   NVARCHAR(200) NULL
);

CREATE TABLE seguranca.PerfilPermissao (
    PerfilId    INT NOT NULL CONSTRAINT FK_PerfilPermissao_Perfil REFERENCES seguranca.Perfil(Id),
    PermissaoId INT NOT NULL CONSTRAINT FK_PerfilPermissao_Permissao REFERENCES seguranca.Permissao(Id),
    CONSTRAINT PK_PerfilPermissao PRIMARY KEY (PerfilId, PermissaoId)
);

CREATE TABLE seguranca.Usuario (
    Id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Usuario PRIMARY KEY,
    ClinicaId         INT           NOT NULL CONSTRAINT FK_Usuario_Clinica REFERENCES seguranca.Clinica(Id),
    Nome              NVARCHAR(150) NOT NULL,
    Email             VARCHAR(254)  NOT NULL CONSTRAINT UQ_Usuario_Email UNIQUE,   -- login: único entre todas as clínicas
    SenhaHash         VARCHAR(500)  NOT NULL,          -- hash gerado pela aplicação (ASP.NET Identity / PBKDF2)
    Ativo             BIT           NOT NULL CONSTRAINT DF_Usuario_Ativo DEFAULT (1),
    TentativasFalhas  TINYINT       NOT NULL CONSTRAINT DF_Usuario_Tentativas DEFAULT (0),
    BloqueadoAte      DATETIME2(0)  NULL,
    UltimoAcessoEm    DATETIME2(0)  NULL,
    Idioma            VARCHAR(5)    NULL,        -- pt-BR, en-US, es-ES, fr-FR; NULL = padrão do sistema (pt-BR)
    CriadoEm          DATETIME2(0)  NOT NULL CONSTRAINT DF_Usuario_CriadoEm DEFAULT (SYSDATETIME()),
    AtualizadoEm      DATETIME2(0)  NULL,
    CONSTRAINT UQ_Usuario_Clinica UNIQUE (ClinicaId, Id)
);

CREATE TABLE seguranca.UsuarioPerfil (
    UsuarioId   INT NOT NULL CONSTRAINT FK_UsuarioPerfil_Usuario REFERENCES seguranca.Usuario(Id),
    PerfilId    INT NOT NULL CONSTRAINT FK_UsuarioPerfil_Perfil  REFERENCES seguranca.Perfil(Id),
    CONSTRAINT PK_UsuarioPerfil PRIMARY KEY (UsuarioId, PerfilId)
);

-- Administradores do sistema (área /Admin): login separado dos usuários das clínicas
CREATE TABLE seguranca.AdministradorSistema (
    Id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AdministradorSistema PRIMARY KEY,
    Nome              NVARCHAR(150) NOT NULL,
    Email             VARCHAR(254)  NOT NULL CONSTRAINT UQ_AdministradorSistema_Email UNIQUE,
    SenhaHash         VARCHAR(500)  NOT NULL,
    Ativo             BIT           NOT NULL CONSTRAINT DF_AdministradorSistema_Ativo DEFAULT (1),
    TentativasFalhas  TINYINT       NOT NULL CONSTRAINT DF_AdministradorSistema_Tentativas DEFAULT (0),
    BloqueadoAte      DATETIME2(0)  NULL,
    UltimoAcessoEm    DATETIME2(0)  NULL,
    Idioma            VARCHAR(5)    NULL,        -- idioma escolhido no seletor (NULL = pt-BR)
    CriadoEm          DATETIME2(0)  NOT NULL CONSTRAINT DF_AdministradorSistema_CriadoEm DEFAULT (SYSDATETIME()),
    AtualizadoEm      DATETIME2(0)  NULL
);

-- Trilha de auditoria (LGPD: quem acessou/alterou dado sensível, quando e de onde)
CREATE TABLE seguranca.LogAuditoria (
    Id               BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_LogAuditoria PRIMARY KEY,
    ClinicaId        INT           NULL CONSTRAINT FK_LogAuditoria_Clinica REFERENCES seguranca.Clinica(Id),
    UsuarioId        INT           NULL CONSTRAINT FK_LogAuditoria_Usuario REFERENCES seguranca.Usuario(Id),
    AdministradorSistemaId INT     NULL CONSTRAINT FK_LogAuditoria_AdminSistema REFERENCES seguranca.AdministradorSistema(Id), -- ações feitas na área /Admin
    Acao             VARCHAR(20)   NOT NULL,           -- 'LEITURA','INCLUSAO','ALTERACAO','EXCLUSAO','LOGIN'
    Entidade         VARCHAR(100)  NOT NULL,           -- ex.: 'prontuario.Evolucao'
    EntidadeId       VARCHAR(50)   NULL,
    DadosAnteriores  NVARCHAR(MAX) NULL,               -- JSON
    DadosNovos       NVARCHAR(MAX) NULL,               -- JSON
    EnderecoIp       VARCHAR(45)   NULL,
    OcorridoEm       DATETIME2(3)  NOT NULL CONSTRAINT DF_LogAuditoria_OcorridoEm DEFAULT (SYSDATETIME()),
    CONSTRAINT CK_LogAuditoria_Acao CHECK (Acao IN ('LEITURA','INCLUSAO','ALTERACAO','EXCLUSAO','LOGIN','LOGOUT','IMPRESSAO','ALTERACAO_SENHA'))
);
CREATE INDEX IX_LogAuditoria_Entidade ON seguranca.LogAuditoria (ClinicaId, Entidade, EntidadeId);
CREATE INDEX IX_LogAuditoria_Usuario  ON seguranca.LogAuditoria (UsuarioId, OcorridoEm);

/* =====================================================================
   CADASTROS
   ===================================================================== */

CREATE TABLE cadastro.Especialidade (
    Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Especialidade PRIMARY KEY,
    ClinicaId   INT           NOT NULL CONSTRAINT FK_Especialidade_Clinica REFERENCES seguranca.Clinica(Id),
    Nome        NVARCHAR(100) NOT NULL,
    CodigoCbo   VARCHAR(10)   NULL,                    -- CBO usado no faturamento TISS
    Ativo       BIT           NOT NULL CONSTRAINT DF_Especialidade_Ativo DEFAULT (1),
    CONSTRAINT UQ_Especialidade_Nome UNIQUE (ClinicaId, Nome),
    CONSTRAINT UQ_Especialidade_Clinica UNIQUE (ClinicaId, Id)
);

CREATE TABLE cadastro.Medico (
    Id                        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Medico PRIMARY KEY,
    ClinicaId                 INT           NOT NULL CONSTRAINT FK_Medico_Clinica REFERENCES seguranca.Clinica(Id),
    UsuarioId                 INT           NULL,
    Nome                      NVARCHAR(150) NOT NULL,
    Crm                       VARCHAR(15)   NOT NULL,
    UfCrm                     CHAR(2)       NOT NULL,
    Cpf                       CHAR(11)      NULL,
    Email                     VARCHAR(254)  NULL,
    Telefone                  VARCHAR(20)   NULL,
    DuracaoConsultaPadraoMin  SMALLINT      NOT NULL CONSTRAINT DF_Medico_Duracao DEFAULT (30),
    Ativo                     BIT           NOT NULL CONSTRAINT DF_Medico_Ativo DEFAULT (1),
    CriadoEm                  DATETIME2(0)  NOT NULL CONSTRAINT DF_Medico_CriadoEm DEFAULT (SYSDATETIME()),
    AtualizadoEm              DATETIME2(0)  NULL,
    CONSTRAINT UQ_Medico_Crm UNIQUE (ClinicaId, Crm, UfCrm),   -- o mesmo médico pode atender em mais de uma clínica
    CONSTRAINT UQ_Medico_Clinica UNIQUE (ClinicaId, Id),
    CONSTRAINT FK_Medico_Usuario FOREIGN KEY (ClinicaId, UsuarioId) REFERENCES seguranca.Usuario (ClinicaId, Id),
    CONSTRAINT CK_Medico_Duracao CHECK (DuracaoConsultaPadraoMin BETWEEN 5 AND 240)
);
-- Um usuário pode estar ligado a no máximo um médico
CREATE UNIQUE INDEX UX_Medico_Usuario ON cadastro.Medico (UsuarioId) WHERE UsuarioId IS NOT NULL;
CREATE INDEX IX_Medico_Nome ON cadastro.Medico (ClinicaId, Nome);

CREATE TABLE cadastro.MedicoEspecialidade (
    ClinicaId        INT NOT NULL,
    MedicoId         INT NOT NULL,
    EspecialidadeId  INT NOT NULL,
    Rqe              VARCHAR(15) NULL,                 -- Registro de Qualificação de Especialista
    Principal        BIT NOT NULL CONSTRAINT DF_MedicoEspecialidade_Principal DEFAULT (0),
    CONSTRAINT PK_MedicoEspecialidade PRIMARY KEY (MedicoId, EspecialidadeId),
    CONSTRAINT FK_MedicoEspecialidade_Medico        FOREIGN KEY (ClinicaId, MedicoId)        REFERENCES cadastro.Medico (ClinicaId, Id),
    CONSTRAINT FK_MedicoEspecialidade_Especialidade FOREIGN KEY (ClinicaId, EspecialidadeId) REFERENCES cadastro.Especialidade (ClinicaId, Id)
);

CREATE TABLE cadastro.Convenio (
    Id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Convenio PRIMARY KEY,
    ClinicaId         INT           NOT NULL CONSTRAINT FK_Convenio_Clinica REFERENCES seguranca.Clinica(Id),
    Nome              NVARCHAR(100) NOT NULL,
    RegistroAns       VARCHAR(10)   NULL,
    Cnpj              CHAR(14)      NULL,
    Telefone          VARCHAR(20)   NULL,
    PrazoRetornoDias  SMALLINT      NOT NULL CONSTRAINT DF_Convenio_PrazoRetorno DEFAULT (30),
    Ativo             BIT           NOT NULL CONSTRAINT DF_Convenio_Ativo DEFAULT (1),
    CONSTRAINT UQ_Convenio_Nome UNIQUE (ClinicaId, Nome),
    CONSTRAINT UQ_Convenio_Clinica UNIQUE (ClinicaId, Id)
);

CREATE TABLE cadastro.Plano (
    Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Plano PRIMARY KEY,
    ClinicaId   INT           NOT NULL,
    ConvenioId  INT           NOT NULL,
    Nome        NVARCHAR(100) NOT NULL,
    Codigo      VARCHAR(20)   NULL,
    Ativo       BIT           NOT NULL CONSTRAINT DF_Plano_Ativo DEFAULT (1),
    CONSTRAINT UQ_Plano_Convenio_Nome UNIQUE (ConvenioId, Nome),
    CONSTRAINT UQ_Plano_Clinica UNIQUE (ClinicaId, Id),
    CONSTRAINT FK_Plano_Convenio FOREIGN KEY (ClinicaId, ConvenioId) REFERENCES cadastro.Convenio (ClinicaId, Id)
);

-- Convênios aceitos por cada médico
CREATE TABLE cadastro.MedicoConvenio (
    ClinicaId   INT NOT NULL,
    MedicoId    INT NOT NULL,
    ConvenioId  INT NOT NULL,
    CONSTRAINT PK_MedicoConvenio PRIMARY KEY (MedicoId, ConvenioId),
    CONSTRAINT FK_MedicoConvenio_Medico   FOREIGN KEY (ClinicaId, MedicoId)   REFERENCES cadastro.Medico (ClinicaId, Id),
    CONSTRAINT FK_MedicoConvenio_Convenio FOREIGN KEY (ClinicaId, ConvenioId) REFERENCES cadastro.Convenio (ClinicaId, Id)
);

CREATE TABLE cadastro.Paciente (
    Id               INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Paciente PRIMARY KEY,
    ClinicaId        INT           NOT NULL CONSTRAINT FK_Paciente_Clinica REFERENCES seguranca.Clinica(Id),
    Nome             NVARCHAR(150) NOT NULL,
    NomeSocial       NVARCHAR(150) NULL,
    Cpf              CHAR(11)      NULL,               -- opcional (recém-nascidos, estrangeiros)
    Cns              CHAR(15)      NULL,               -- Cartão Nacional de Saúde
    DataNascimento   DATE          NOT NULL,
    Sexo             CHAR(1)       NOT NULL,           -- M, F, I (intersexo/ignorado)
    Email            VARCHAR(254)  NULL,
    Telefone         VARCHAR(20)   NULL,
    Celular          VARCHAR(20)   NULL,
    Cep              CHAR(8)       NULL,
    Logradouro       NVARCHAR(150) NULL,
    Numero           NVARCHAR(10)  NULL,
    Complemento      NVARCHAR(60)  NULL,
    Bairro           NVARCHAR(80)  NULL,
    Cidade           NVARCHAR(80)  NULL,
    Uf               CHAR(2)       NULL,
    NomeResponsavel  NVARCHAR(150) NULL,
    CpfResponsavel   CHAR(11)      NULL,
    Observacoes      NVARCHAR(500) NULL,
    ConsentimentoLgpdEm DATETIME2(0) NULL,
    Ativo            BIT           NOT NULL CONSTRAINT DF_Paciente_Ativo DEFAULT (1),
    CriadoEm         DATETIME2(0)  NOT NULL CONSTRAINT DF_Paciente_CriadoEm DEFAULT (SYSDATETIME()),
    AtualizadoEm     DATETIME2(0)  NULL,
    CONSTRAINT UQ_Paciente_Clinica UNIQUE (ClinicaId, Id),
    CONSTRAINT CK_Paciente_Sexo CHECK (Sexo IN ('M','F','I'))
);
-- O mesmo CPF pode existir em clínicas diferentes (cada clínica tem o próprio cadastro)
CREATE UNIQUE INDEX UX_Paciente_Cpf ON cadastro.Paciente (ClinicaId, Cpf) WHERE Cpf IS NOT NULL;
CREATE INDEX IX_Paciente_Nome ON cadastro.Paciente (ClinicaId, Nome) INCLUDE (DataNascimento, Celular);

-- Carteirinhas do paciente (um paciente pode ter mais de um plano)
CREATE TABLE cadastro.PacientePlano (
    Id                   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PacientePlano PRIMARY KEY,
    ClinicaId            INT          NOT NULL,
    PacienteId           INT          NOT NULL,
    PlanoId              INT          NOT NULL,
    NumeroCarteirinha    VARCHAR(30)  NOT NULL,
    ValidadeCarteirinha  DATE         NULL,
    Titular              BIT          NOT NULL CONSTRAINT DF_PacientePlano_Titular DEFAULT (1),
    Ativo                BIT          NOT NULL CONSTRAINT DF_PacientePlano_Ativo DEFAULT (1),
    CONSTRAINT UQ_PacientePlano UNIQUE (PlanoId, NumeroCarteirinha),
    CONSTRAINT UQ_PacientePlano_Clinica UNIQUE (ClinicaId, Id),
    CONSTRAINT FK_PacientePlano_Paciente FOREIGN KEY (ClinicaId, PacienteId) REFERENCES cadastro.Paciente (ClinicaId, Id),
    CONSTRAINT FK_PacientePlano_Plano    FOREIGN KEY (ClinicaId, PlanoId)    REFERENCES cadastro.Plano (ClinicaId, Id)
);
CREATE INDEX IX_PacientePlano_Paciente ON cadastro.PacientePlano (PacienteId);

/* =====================================================================
   AGENDA
   ===================================================================== */

-- Grade semanal do médico (ex.: seg 08:00-12:00, slots de 30 min)
CREATE TABLE agenda.HorarioAtendimento (
    Id               INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_HorarioAtendimento PRIMARY KEY,
    ClinicaId        INT      NOT NULL,
    MedicoId         INT      NOT NULL,
    EspecialidadeId  INT      NULL,
    DiaSemana        TINYINT  NOT NULL,                -- 0=domingo ... 6=sábado (igual a DayOfWeek do .NET)
    HoraInicio       TIME(0)  NOT NULL,
    HoraFim          TIME(0)  NOT NULL,
    DuracaoSlotMin   SMALLINT NOT NULL,
    VigenciaInicio   DATE     NOT NULL,
    VigenciaFim      DATE     NULL,
    Ativo            BIT      NOT NULL CONSTRAINT DF_HorarioAtendimento_Ativo DEFAULT (1),
    CONSTRAINT FK_HorarioAtendimento_Medico        FOREIGN KEY (ClinicaId, MedicoId)        REFERENCES cadastro.Medico (ClinicaId, Id),
    CONSTRAINT FK_HorarioAtendimento_Especialidade FOREIGN KEY (ClinicaId, EspecialidadeId) REFERENCES cadastro.Especialidade (ClinicaId, Id),
    CONSTRAINT CK_HorarioAtendimento_Dia     CHECK (DiaSemana BETWEEN 0 AND 6),
    CONSTRAINT CK_HorarioAtendimento_Horas   CHECK (HoraFim > HoraInicio),
    CONSTRAINT CK_HorarioAtendimento_Slot    CHECK (DuracaoSlotMin BETWEEN 5 AND 240),
    CONSTRAINT CK_HorarioAtendimento_Vigencia CHECK (VigenciaFim IS NULL OR VigenciaFim >= VigenciaInicio)
);
CREATE INDEX IX_HorarioAtendimento_Medico ON agenda.HorarioAtendimento (MedicoId, DiaSemana) WHERE Ativo = 1;

-- Férias, congressos, feriados: períodos em que a grade não vale
CREATE TABLE agenda.BloqueioAgenda (
    Id                  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BloqueioAgenda PRIMARY KEY,
    ClinicaId           INT           NOT NULL CONSTRAINT FK_BloqueioAgenda_Clinica REFERENCES seguranca.Clinica(Id),
    MedicoId            INT           NULL, -- NULL = clínica inteira
    Inicio              DATETIME2(0)  NOT NULL,
    Fim                 DATETIME2(0)  NOT NULL,
    Motivo              NVARCHAR(200) NULL,
    CriadoPorUsuarioId  INT           NULL CONSTRAINT FK_BloqueioAgenda_Usuario REFERENCES seguranca.Usuario(Id),
    CriadoEm            DATETIME2(0)  NOT NULL CONSTRAINT DF_BloqueioAgenda_CriadoEm DEFAULT (SYSDATETIME()),
    CONSTRAINT FK_BloqueioAgenda_Medico FOREIGN KEY (ClinicaId, MedicoId) REFERENCES cadastro.Medico (ClinicaId, Id),
    CONSTRAINT CK_BloqueioAgenda_Periodo CHECK (Fim > Inicio)
);
CREATE INDEX IX_BloqueioAgenda_Medico ON agenda.BloqueioAgenda (ClinicaId, MedicoId, Inicio, Fim);

-- Tabelas de domínio (IDs fixos = enums no C#)
CREATE TABLE agenda.StatusAgendamento (
    Id      TINYINT      NOT NULL CONSTRAINT PK_StatusAgendamento PRIMARY KEY,
    Codigo  VARCHAR(20)  NOT NULL CONSTRAINT UQ_StatusAgendamento_Codigo UNIQUE,
    Nome    NVARCHAR(50) NOT NULL,
    OcupaHorario BIT     NOT NULL                     -- 1 = conta como horário ocupado
);

CREATE TABLE agenda.TipoAtendimento (
    Id      TINYINT      NOT NULL CONSTRAINT PK_TipoAtendimento PRIMARY KEY,
    Codigo  VARCHAR(20)  NOT NULL CONSTRAINT UQ_TipoAtendimento_Codigo UNIQUE,
    Nome    NVARCHAR(50) NOT NULL
);

CREATE TABLE agenda.Agendamento (
    Id                    INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Agendamento PRIMARY KEY,
    ClinicaId             INT           NOT NULL,
    PacienteId            INT           NOT NULL,
    MedicoId              INT           NOT NULL,
    EspecialidadeId       INT           NOT NULL,
    PacientePlanoId       INT           NULL,     -- NULL = particular
    TipoAtendimentoId     TINYINT       NOT NULL CONSTRAINT FK_Agendamento_Tipo          REFERENCES agenda.TipoAtendimento(Id),
    StatusId              TINYINT       NOT NULL CONSTRAINT FK_Agendamento_Status        REFERENCES agenda.StatusAgendamento(Id),
    Inicio                DATETIME2(0)  NOT NULL,
    Fim                   DATETIME2(0)  NOT NULL,
    Valor                 DECIMAL(10,2) NULL,          -- valor cobrado (particular) ou valor de referência
    Observacoes           NVARCHAR(500) NULL,
    AgendamentoOrigemId   INT           NULL,     -- retorno ou remarcação
    ConfirmadoEm          DATETIME2(0)  NULL,
    ChegadaEm             DATETIME2(0)  NULL,
    InicioAtendimentoEm   DATETIME2(0)  NULL,
    FimAtendimentoEm      DATETIME2(0)  NULL,
    CanceladoEm           DATETIME2(0)  NULL,
    MotivoCancelamento    NVARCHAR(200) NULL,
    CriadoPorUsuarioId    INT           NULL CONSTRAINT FK_Agendamento_CriadoPor REFERENCES seguranca.Usuario(Id),
    CriadoEm              DATETIME2(0)  NOT NULL CONSTRAINT DF_Agendamento_CriadoEm DEFAULT (SYSDATETIME()),
    AtualizadoEm          DATETIME2(0)  NULL,
    VersaoLinha           ROWVERSION    NOT NULL,      -- concorrência otimista (EF Core [Timestamp])
    CONSTRAINT UQ_Agendamento_Clinica UNIQUE (ClinicaId, Id),
    CONSTRAINT FK_Agendamento_Paciente      FOREIGN KEY (ClinicaId, PacienteId)          REFERENCES cadastro.Paciente (ClinicaId, Id),
    CONSTRAINT FK_Agendamento_Medico        FOREIGN KEY (ClinicaId, MedicoId)            REFERENCES cadastro.Medico (ClinicaId, Id),
    CONSTRAINT FK_Agendamento_Especialidade FOREIGN KEY (ClinicaId, EspecialidadeId)     REFERENCES cadastro.Especialidade (ClinicaId, Id),
    CONSTRAINT FK_Agendamento_PacientePlano FOREIGN KEY (ClinicaId, PacientePlanoId)     REFERENCES cadastro.PacientePlano (ClinicaId, Id),
    CONSTRAINT CK_Agendamento_Periodo CHECK (Fim > Inicio)
);
-- Auto-referência criada depois da tabela (precisa da UQ_Agendamento_Clinica já existente)
ALTER TABLE agenda.Agendamento ADD CONSTRAINT FK_Agendamento_Origem
    FOREIGN KEY (ClinicaId, AgendamentoOrigemId) REFERENCES agenda.Agendamento (ClinicaId, Id);

-- Impede dois agendamentos ativos começando no mesmo horário para o mesmo médico.
-- Sobreposição parcial (ex.: 09:00-09:40 x 09:30-10:00) é validada na aplicação,
-- dentro de transação SERIALIZABLE ou com sp_getapplock por médico.
-- Os IDs abaixo são os status com OcupaHorario = 1 (ver carga inicial).
CREATE UNIQUE INDEX UX_Agendamento_MedicoInicio
    ON agenda.Agendamento (MedicoId, Inicio)
    WHERE StatusId IN (1, 2, 3, 4, 5);
CREATE INDEX IX_Agendamento_Medico_Periodo   ON agenda.Agendamento (MedicoId, Inicio) INCLUDE (Fim, StatusId, PacienteId);
CREATE INDEX IX_Agendamento_Paciente         ON agenda.Agendamento (PacienteId, Inicio);
CREATE INDEX IX_Agendamento_Inicio_Status    ON agenda.Agendamento (ClinicaId, Inicio, StatusId) INCLUDE (MedicoId, EspecialidadeId, PacientePlanoId, TipoAtendimentoId, Valor); -- dashboard/relatórios

-- Histórico de mudanças de status (base para taxa de faltas, tempo de espera etc.)
CREATE TABLE agenda.AgendamentoHistoricoStatus (
    Id                BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AgendamentoHistoricoStatus PRIMARY KEY,
    AgendamentoId     INT           NOT NULL CONSTRAINT FK_AgHist_Agendamento REFERENCES agenda.Agendamento(Id),
    StatusAnteriorId  TINYINT       NULL     CONSTRAINT FK_AgHist_StatusAnterior REFERENCES agenda.StatusAgendamento(Id),
    StatusNovoId      TINYINT       NOT NULL CONSTRAINT FK_AgHist_StatusNovo     REFERENCES agenda.StatusAgendamento(Id),
    UsuarioId         INT           NULL     CONSTRAINT FK_AgHist_Usuario        REFERENCES seguranca.Usuario(Id),
    Observacao        NVARCHAR(200) NULL,
    AlteradoEm        DATETIME2(0)  NOT NULL CONSTRAINT DF_AgHist_AlteradoEm DEFAULT (SYSDATETIME())
);
CREATE INDEX IX_AgHist_Agendamento ON agenda.AgendamentoHistoricoStatus (AgendamentoId, AlteradoEm);

/* =====================================================================
   PRONTUÁRIO
   Regra (CFM 1.638/2002 e 1.821/2007): registro assinado não é alterado
   nem excluído. Correções geram nova evolução apontando para a original.
   ===================================================================== */

-- Resumo clínico permanente do paciente (1:1 com Paciente)
CREATE TABLE prontuario.ResumoClinico (
    PacienteId              INT            NOT NULL CONSTRAINT PK_ResumoClinico PRIMARY KEY,
    ClinicaId               INT            NOT NULL,
    TipoSanguineo           VARCHAR(3)     NULL,
    Alergias                NVARCHAR(1000) NULL,
    Comorbidades            NVARCHAR(1000) NULL,
    MedicamentosEmUso       NVARCHAR(1000) NULL,
    AntecedentesPessoais    NVARCHAR(2000) NULL,
    AntecedentesFamiliares  NVARCHAR(2000) NULL,
    Habitos                 NVARCHAR(1000) NULL,
    AtualizadoPorUsuarioId  INT            NULL CONSTRAINT FK_ResumoClinico_Usuario REFERENCES seguranca.Usuario(Id),
    AtualizadoEm            DATETIME2(0)   NOT NULL CONSTRAINT DF_ResumoClinico_AtualizadoEm DEFAULT (SYSDATETIME()),
    CONSTRAINT FK_ResumoClinico_Paciente FOREIGN KEY (ClinicaId, PacienteId) REFERENCES cadastro.Paciente (ClinicaId, Id),
    CONSTRAINT CK_ResumoClinico_TipoSanguineo CHECK (TipoSanguineo IN ('A+','A-','B+','B-','AB+','AB-','O+','O-'))
);

CREATE TABLE prontuario.Evolucao (
    Id                   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Evolucao PRIMARY KEY,
    ClinicaId            INT            NOT NULL,
    PacienteId           INT            NOT NULL,
    MedicoId             INT            NOT NULL,
    AgendamentoId        INT            NULL,
    EvolucaoRetificadaId INT            NULL,
    DataHora             DATETIME2(0)   NOT NULL CONSTRAINT DF_Evolucao_DataHora DEFAULT (SYSDATETIME()),
    QueixaPrincipal      NVARCHAR(500)  NULL,
    HistoriaDoencaAtual  NVARCHAR(MAX)  NULL,
    ExameFisico          NVARCHAR(MAX)  NULL,
    HipoteseDiagnostica  NVARCHAR(1000) NULL,
    Conduta              NVARCHAR(MAX)  NULL,
    -- Sinais vitais
    PesoKg               DECIMAL(5,2)   NULL,
    AlturaCm             DECIMAL(5,1)   NULL,
    PressaoSistolica     SMALLINT       NULL,
    PressaoDiastolica    SMALLINT       NULL,
    FrequenciaCardiaca   SMALLINT       NULL,
    TemperaturaC         DECIMAL(4,1)   NULL,
    SaturacaoO2          TINYINT        NULL,
    -- Assinatura
    Situacao             CHAR(1)        NOT NULL CONSTRAINT DF_Evolucao_Situacao DEFAULT ('R'), -- R=rascunho, A=assinada
    AssinadaEm           DATETIME2(0)   NULL,
    HashConteudo         VARBINARY(32)  NULL,          -- SHA-256 do conteúdo no momento da assinatura
    CriadoEm             DATETIME2(0)   NOT NULL CONSTRAINT DF_Evolucao_CriadoEm DEFAULT (SYSDATETIME()),
    AtualizadoEm         DATETIME2(0)   NULL,
    CONSTRAINT UQ_Evolucao_Clinica UNIQUE (ClinicaId, Id),
    CONSTRAINT FK_Evolucao_Paciente    FOREIGN KEY (ClinicaId, PacienteId)           REFERENCES cadastro.Paciente (ClinicaId, Id),
    CONSTRAINT FK_Evolucao_Medico      FOREIGN KEY (ClinicaId, MedicoId)             REFERENCES cadastro.Medico (ClinicaId, Id),
    CONSTRAINT FK_Evolucao_Agendamento FOREIGN KEY (ClinicaId, AgendamentoId)        REFERENCES agenda.Agendamento (ClinicaId, Id),
    CONSTRAINT CK_Evolucao_Situacao  CHECK (Situacao IN ('R','A')),
    CONSTRAINT CK_Evolucao_Assinatura CHECK ((Situacao = 'R' AND AssinadaEm IS NULL) OR (Situacao = 'A' AND AssinadaEm IS NOT NULL)),
    CONSTRAINT CK_Evolucao_SatO2     CHECK (SaturacaoO2 IS NULL OR SaturacaoO2 BETWEEN 0 AND 100)
);
ALTER TABLE prontuario.Evolucao ADD CONSTRAINT FK_Evolucao_Retificada
    FOREIGN KEY (ClinicaId, EvolucaoRetificadaId) REFERENCES prontuario.Evolucao (ClinicaId, Id);
CREATE INDEX IX_Evolucao_Paciente ON prontuario.Evolucao (PacienteId, DataHora DESC);
CREATE INDEX IX_Evolucao_Medico   ON prontuario.Evolucao (MedicoId, DataHora);
CREATE UNIQUE INDEX UX_Evolucao_Agendamento ON prontuario.Evolucao (AgendamentoId)
    WHERE AgendamentoId IS NOT NULL AND EvolucaoRetificadaId IS NULL;
GO

-- Bloqueia UPDATE/DELETE em evolução já assinada (defesa no banco, além da aplicação)
CREATE TRIGGER prontuario.TR_Evolucao_Imutavel
ON prontuario.Evolucao
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted WHERE Situacao = 'A')
    BEGIN
        THROW 50001, 'Evolução assinada não pode ser alterada ou excluída. Registre uma retificação.', 1;
    END
END;
GO

-- Tabela CID-10 (carga a partir da base do DATASUS)
CREATE TABLE prontuario.Cid (
    Codigo     VARCHAR(6)    NOT NULL CONSTRAINT PK_Cid PRIMARY KEY,   -- ex.: 'J06.9'
    Descricao  NVARCHAR(300) NOT NULL
);

CREATE TABLE prontuario.EvolucaoDiagnostico (
    EvolucaoId  INT        NOT NULL CONSTRAINT FK_EvolucaoDiagnostico_Evolucao REFERENCES prontuario.Evolucao(Id),
    CidCodigo   VARCHAR(6) NOT NULL CONSTRAINT FK_EvolucaoDiagnostico_Cid      REFERENCES prontuario.Cid(Codigo),
    Principal   BIT        NOT NULL CONSTRAINT DF_EvolucaoDiagnostico_Principal DEFAULT (0),
    CONSTRAINT PK_EvolucaoDiagnostico PRIMARY KEY (EvolucaoId, CidCodigo)
);
CREATE INDEX IX_EvolucaoDiagnostico_Cid ON prontuario.EvolucaoDiagnostico (CidCodigo);

CREATE TABLE prontuario.Prescricao (
    Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Prescricao PRIMARY KEY,
    ClinicaId     INT           NOT NULL,
    EvolucaoId    INT           NULL,
    PacienteId    INT           NOT NULL,
    MedicoId      INT           NOT NULL,
    Tipo          CHAR(1)       NOT NULL CONSTRAINT DF_Prescricao_Tipo DEFAULT ('S'), -- S=simples, C=controle especial, A=antimicrobiano
    EmitidaEm     DATETIME2(0)  NOT NULL CONSTRAINT DF_Prescricao_EmitidaEm DEFAULT (SYSDATETIME()),
    ValidadeDias  SMALLINT      NULL,
    Observacoes   NVARCHAR(500) NULL,
    AssinadaEm    DATETIME2(0)  NULL,
    CONSTRAINT FK_Prescricao_Evolucao FOREIGN KEY (ClinicaId, EvolucaoId) REFERENCES prontuario.Evolucao (ClinicaId, Id),
    CONSTRAINT FK_Prescricao_Paciente FOREIGN KEY (ClinicaId, PacienteId) REFERENCES cadastro.Paciente (ClinicaId, Id),
    CONSTRAINT FK_Prescricao_Medico   FOREIGN KEY (ClinicaId, MedicoId)   REFERENCES cadastro.Medico (ClinicaId, Id),
    CONSTRAINT CK_Prescricao_Tipo CHECK (Tipo IN ('S','C','A'))
);
CREATE INDEX IX_Prescricao_Paciente ON prontuario.Prescricao (PacienteId, EmitidaEm DESC);

CREATE TABLE prontuario.PrescricaoItem (
    Id                 INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PrescricaoItem PRIMARY KEY,
    PrescricaoId       INT           NOT NULL CONSTRAINT FK_PrescricaoItem_Prescricao REFERENCES prontuario.Prescricao(Id) ON DELETE CASCADE,
    Ordem              TINYINT       NOT NULL,
    Medicamento        NVARCHAR(200) NOT NULL,
    Concentracao       NVARCHAR(50)  NULL,          -- ex.: '500 mg'
    FormaFarmaceutica  NVARCHAR(50)  NULL,          -- ex.: 'comprimido'
    ViaAdministracao   NVARCHAR(30)  NULL,          -- ex.: 'oral'
    Posologia          NVARCHAR(300) NOT NULL,      -- ex.: '1 comprimido de 8/8h por 7 dias'
    Quantidade         NVARCHAR(50)  NULL,          -- ex.: '1 caixa'
    UsoContinuo        BIT           NOT NULL CONSTRAINT DF_PrescricaoItem_UsoContinuo DEFAULT (0),
    CONSTRAINT UQ_PrescricaoItem_Ordem UNIQUE (PrescricaoId, Ordem)
);

-- Arquivos ficam em pasta no servidor IIS (fora do wwwroot), uma subpasta por clínica; o banco guarda metadados e hash
CREATE TABLE prontuario.Anexo (
    Id                   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Anexo PRIMARY KEY,
    ClinicaId            INT            NOT NULL,
    PacienteId           INT            NOT NULL,
    EvolucaoId           INT            NULL,
    Tipo                 VARCHAR(20)    NOT NULL,     -- EXAME, IMAGEM, LAUDO, TERMO, DOCUMENTO, OUTRO
    Descricao            NVARCHAR(200)  NULL,
    NomeArquivo          NVARCHAR(255)  NOT NULL,
    ContentType          VARCHAR(100)   NOT NULL,
    TamanhoBytes         BIGINT         NOT NULL,
    CaminhoArmazenamento NVARCHAR(500)  NOT NULL,
    HashSha256           VARBINARY(32)  NOT NULL,
    EnviadoPorUsuarioId  INT            NULL CONSTRAINT FK_Anexo_Usuario REFERENCES seguranca.Usuario(Id),
    EnviadoEm            DATETIME2(0)   NOT NULL CONSTRAINT DF_Anexo_EnviadoEm DEFAULT (SYSDATETIME()),
    Ativo                BIT            NOT NULL CONSTRAINT DF_Anexo_Ativo DEFAULT (1),
    CONSTRAINT FK_Anexo_Paciente FOREIGN KEY (ClinicaId, PacienteId) REFERENCES cadastro.Paciente (ClinicaId, Id),
    CONSTRAINT FK_Anexo_Evolucao FOREIGN KEY (ClinicaId, EvolucaoId) REFERENCES prontuario.Evolucao (ClinicaId, Id),
    CONSTRAINT CK_Anexo_Tipo CHECK (Tipo IN ('EXAME','IMAGEM','LAUDO','TERMO','DOCUMENTO','OUTRO')),
    CONSTRAINT CK_Anexo_Tamanho CHECK (TamanhoBytes > 0)
);
CREATE INDEX IX_Anexo_Paciente ON prontuario.Anexo (ClinicaId, PacienteId, EnviadoEm DESC);

-- Documentos emitidos ao paciente; o texto fica gravado já preenchido, como foi impresso (não é alterado nem apagado)
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

-- Modelos de texto de cada clínica para os documentos (campos como {paciente} e {dias} são preenchidos na emissão)
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

/* =====================================================================
   VIEWS PARA DASHBOARD / RELATÓRIOS
   ===================================================================== */

CREATE VIEW agenda.vw_AgendaDetalhada
AS
SELECT  a.ClinicaId,
        a.Id,
        a.Inicio,
        a.Fim,
        CAST(a.Inicio AS DATE)             AS Data,
        s.Codigo                           AS Status,
        t.Codigo                           AS TipoAtendimento,
        m.Id                               AS MedicoId,
        m.Nome                             AS Medico,
        e.Nome                             AS Especialidade,
        p.Id                               AS PacienteId,
        COALESCE(p.NomeSocial, p.Nome)     AS Paciente,
        p.Celular,
        c.Nome                             AS Convenio,           -- NULL = particular
        pl.Nome                            AS Plano,
        a.Valor,
        DATEDIFF(MINUTE, a.ChegadaEm, a.InicioAtendimentoEm) AS EsperaMin
FROM    agenda.Agendamento a
JOIN    agenda.StatusAgendamento s  ON s.Id = a.StatusId
JOIN    agenda.TipoAtendimento   t  ON t.Id = a.TipoAtendimentoId
JOIN    cadastro.Medico          m  ON m.Id = a.MedicoId
JOIN    cadastro.Especialidade   e  ON e.Id = a.EspecialidadeId
JOIN    cadastro.Paciente        p  ON p.Id = a.PacienteId
LEFT JOIN cadastro.PacientePlano pp ON pp.Id = a.PacientePlanoId
LEFT JOIN cadastro.Plano         pl ON pl.Id = pp.PlanoId
LEFT JOIN cadastro.Convenio      c  ON c.Id = pl.ConvenioId;
GO

-- Indicadores mensais por clínica e médico: volume, faltas, cancelamentos, faturamento previsto
CREATE VIEW agenda.vw_IndicadoresMensais
AS
SELECT  a.ClinicaId,
        DATEFROMPARTS(YEAR(a.Inicio), MONTH(a.Inicio), 1)    AS Mes,
        a.MedicoId,
        COUNT(*)                                              AS TotalAgendamentos,
        SUM(CASE WHEN s.Codigo = 'ATENDIDO'  THEN 1 ELSE 0 END) AS Atendidos,
        SUM(CASE WHEN s.Codigo = 'FALTOU'    THEN 1 ELSE 0 END) AS Faltas,
        SUM(CASE WHEN s.Codigo = 'CANCELADO' THEN 1 ELSE 0 END) AS Cancelados,
        SUM(CASE WHEN a.PacientePlanoId IS NULL THEN 1 ELSE 0 END) AS Particulares,
        SUM(CASE WHEN s.Codigo = 'ATENDIDO' THEN ISNULL(a.Valor, 0) ELSE 0 END) AS ValorAtendido
FROM    agenda.Agendamento a
JOIN    agenda.StatusAgendamento s ON s.Id = a.StatusId
GROUP BY a.ClinicaId, DATEFROMPARTS(YEAR(a.Inicio), MONTH(a.Inicio), 1), a.MedicoId;
GO

/* =====================================================================
   CARGA INICIAL (domínios)
   ===================================================================== */

INSERT INTO agenda.StatusAgendamento (Id, Codigo, Nome, OcupaHorario) VALUES
    (1, 'AGENDADO',        N'Agendado',          1),
    (2, 'CONFIRMADO',      N'Confirmado',        1),
    (3, 'AGUARDANDO',      N'Na sala de espera', 1),
    (4, 'EM_ATENDIMENTO',  N'Em atendimento',    1),
    (5, 'ATENDIDO',        N'Atendido',          1),
    (6, 'FALTOU',          N'Faltou',            0),
    (7, 'CANCELADO',       N'Cancelado',         0),
    (8, 'REMARCADO',       N'Remarcado',         0);

INSERT INTO agenda.TipoAtendimento (Id, Codigo, Nome) VALUES
    (1, 'CONSULTA',     N'Consulta'),
    (2, 'RETORNO',      N'Retorno'),
    (3, 'PROCEDIMENTO', N'Procedimento'),
    (4, 'TELECONSULTA', N'Teleconsulta');

-- Perfis padrão (ClinicaId NULL = valem para todos os clientes). O que cada um libera pode ser mudado em /Admin → Perfis de acesso.
INSERT INTO seguranca.Perfil (Nome, Descricao) VALUES
    (N'Administrador', N'Cadastros, usuários, dashboard e relatórios; sem acesso ao prontuário'),
    (N'Medico',        N'Agenda própria, prontuário e relatórios dos próprios atendimentos'),
    (N'Recepcao',      N'Agenda de todos os médicos, pacientes e cadastros; sem acesso ao prontuário');

-- Transações do sistema (catálogo em Consultorio.Domain/Common/Transacoes.cs; o site cadastra as novas ao iniciar)
INSERT INTO seguranca.Permissao (Codigo, Descricao) VALUES
    ('dashboard.ver',            N'Dashboard'),
    ('agenda.ver',               N'Ver agenda'),
    ('agenda.todos_medicos',     N'Agenda de todos os médicos'),
    ('agenda.agendar',           N'Agendar e atender a agenda'),
    ('agenda.bloqueios',         N'Bloqueios de agenda'),
    ('pacientes.ver',            N'Consultar pacientes'),
    ('pacientes.editar',         N'Cadastrar pacientes'),
    ('prontuario.ver',           N'Ver prontuário'),
    ('prontuario.editar',        N'Registrar no prontuário'),
    ('prontuario.anexos',        N'Anexos do prontuário'),
    ('cadastros.medicos',        N'Médicos e horários'),
    ('cadastros.especialidades', N'Especialidades'),
    ('cadastros.convenios',      N'Convênios e planos'),
    ('relatorios.ver',           N'Relatórios'),
    ('relatorios.todos_medicos', N'Dashboard e relatórios de todos os médicos'),
    ('usuarios.gerenciar',       N'Usuários da clínica');

-- Administrador: tudo, exceto o prontuário (dado de saúde)
INSERT INTO seguranca.PerfilPermissao (PerfilId, PermissaoId)
SELECT p.Id, pe.Id FROM seguranca.Perfil p CROSS JOIN seguranca.Permissao pe
WHERE p.Nome = N'Administrador' AND pe.Codigo NOT LIKE 'prontuario.%';

-- Médico
INSERT INTO seguranca.PerfilPermissao (PerfilId, PermissaoId)
SELECT p.Id, pe.Id FROM seguranca.Perfil p JOIN seguranca.Permissao pe
    ON pe.Codigo IN ('dashboard.ver','agenda.ver','agenda.agendar','pacientes.ver','pacientes.editar','prontuario.ver','prontuario.editar','prontuario.anexos','relatorios.ver')
WHERE p.Nome = N'Medico';

-- Recepção
INSERT INTO seguranca.PerfilPermissao (PerfilId, PermissaoId)
SELECT p.Id, pe.Id FROM seguranca.Perfil p JOIN seguranca.Permissao pe
    ON pe.Codigo IN ('agenda.ver','agenda.todos_medicos','agenda.agendar','agenda.bloqueios','pacientes.ver','pacientes.editar','cadastros.medicos','cadastros.especialidades','cadastros.convenios')
WHERE p.Nome = N'Recepcao';
GO
