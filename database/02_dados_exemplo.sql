/* =====================================================================
   OPCIONAL - Dados de exemplo para testar o sistema logo após a instalação.
   Rodar depois do 01_schema.sql, somente em banco de desenvolvimento/teste.
   Cria a clínica "Clínica Exemplo" (cliente) com: 2 especialidades, 1 médico com
   grade de segunda a sexta (8h-12h e 14h-18h), 1 convênio com 2 planos e 2 pacientes.
   A clínica de exemplo NÃO tem usuários: entre na área de administração (/Admin),
   abra Clientes > Clínica Exemplo e crie o administrador dela em "Novo usuário".
   Depois, entrando como esse administrador, crie os demais usuários (médico,
   recepção) em Usuários e vincule o médico em Cadastros > Médicos.
   ===================================================================== */
SET NOCOUNT ON;

IF EXISTS (SELECT 1 FROM seguranca.Clinica WHERE Nome = N'Clínica Exemplo')
BEGIN
    PRINT 'Dados de exemplo já existem. Nada foi feito.';
    RETURN;
END

DECLARE @c INT, @clinicaMedica INT, @cardio INT, @medico INT, @convenio INT, @plano INT, @paciente INT;

INSERT INTO seguranca.Clinica (Nome, Cidade, Uf, MaxMedicos, MaxUsuarios, AcessoAte)
    VALUES (N'Clínica Exemplo', N'São Paulo', 'SP', 3, 10, DATEADD(DAY, 30, CAST(GETDATE() AS DATE)));  -- 30 dias de teste
SET @c = SCOPE_IDENTITY();

INSERT INTO cadastro.Especialidade (ClinicaId, Nome, CodigoCbo) VALUES (@c, N'Clínica Médica', '225125');
SET @clinicaMedica = SCOPE_IDENTITY();
INSERT INTO cadastro.Especialidade (ClinicaId, Nome, CodigoCbo) VALUES (@c, N'Cardiologia', '225120');
SET @cardio = SCOPE_IDENTITY();

INSERT INTO cadastro.Medico (ClinicaId, Nome, Crm, UfCrm, DuracaoConsultaPadraoMin) VALUES (@c, N'Dra. Exemplo da Silva', '000000', 'SP', 30);
SET @medico = SCOPE_IDENTITY();
INSERT INTO cadastro.MedicoEspecialidade (ClinicaId, MedicoId, EspecialidadeId, Principal)
VALUES (@c, @medico, @clinicaMedica, 1), (@c, @medico, @cardio, 0);

INSERT INTO agenda.HorarioAtendimento (ClinicaId, MedicoId, DiaSemana, HoraInicio, HoraFim, DuracaoSlotMin, VigenciaInicio)
SELECT @c, @medico, d.Dia, h.Ini, h.Fim, 30, CAST(GETDATE() AS DATE)
FROM (VALUES (1),(2),(3),(4),(5)) d(Dia)
CROSS JOIN (VALUES (CAST('08:00' AS TIME(0)), CAST('12:00' AS TIME(0))), ('14:00', '18:00')) h(Ini, Fim);

INSERT INTO cadastro.Convenio (ClinicaId, Nome, RegistroAns) VALUES (@c, N'Convênio Exemplo', '000000');
SET @convenio = SCOPE_IDENTITY();
INSERT INTO cadastro.Plano (ClinicaId, ConvenioId, Nome, Codigo) VALUES (@c, @convenio, N'Básico', 'BAS');
SET @plano = SCOPE_IDENTITY();
INSERT INTO cadastro.Plano (ClinicaId, ConvenioId, Nome, Codigo) VALUES (@c, @convenio, N'Executivo', 'EXE');

INSERT INTO cadastro.Paciente (ClinicaId, Nome, DataNascimento, Sexo, Celular, Cidade, Uf, ConsentimentoLgpdEm)
VALUES (@c, N'Maria de Souza (exemplo)', '1985-03-14', 'F', '(11) 99999-0001', N'São Paulo', 'SP', SYSDATETIME());
SET @paciente = SCOPE_IDENTITY();
INSERT INTO cadastro.PacientePlano (ClinicaId, PacienteId, PlanoId, NumeroCarteirinha) VALUES (@c, @paciente, @plano, '123456789');

INSERT INTO cadastro.Paciente (ClinicaId, Nome, DataNascimento, Sexo, Celular, Cidade, Uf, ConsentimentoLgpdEm)
VALUES (@c, N'José Pereira (exemplo)', '1972-11-02', 'M', '(11) 99999-0002', N'São Paulo', 'SP', SYSDATETIME());

PRINT 'Dados de exemplo criados (Clínica Exemplo).';
GO
