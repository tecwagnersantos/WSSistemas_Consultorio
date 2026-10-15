namespace Consultorio.Domain.Common;

/// <summary>Uma transação (tela ou ação) do sistema que pode ser liberada a um perfil de acesso.</summary>
/// <param name="Sensivel">Dá acesso a dados de saúde (LGPD): a tela de perfis mostra um aviso.</param>
public record Transacao(string Codigo, string Grupo, string Nome, string Descricao, bool Sensivel = false);

/// <summary>
/// Catálogo das transações. O código fica em seguranca.Permissao (sincronizado ao iniciar o site) e os perfis
/// de acesso (seguranca.Perfil) recebem as transações na área /Admin → Perfis de acesso.
/// Ao criar uma tela nova: inclua a transação aqui e proteja o controller com [Authorize(Policy = Transacoes.Xxx)].
/// </summary>
public static class Transacoes
{
    public const string DashboardVer = "dashboard.ver";
    public const string AgendaVer = "agenda.ver";
    public const string AgendaTodosMedicos = "agenda.todos_medicos";
    public const string AgendaAgendar = "agenda.agendar";
    public const string AgendaBloqueios = "agenda.bloqueios";
    public const string PacientesVer = "pacientes.ver";
    public const string PacientesEditar = "pacientes.editar";
    public const string ProntuarioVer = "prontuario.ver";
    public const string ProntuarioEditar = "prontuario.editar";
    public const string ProntuarioAnexos = "prontuario.anexos";
    public const string CadastrosMedicos = "cadastros.medicos";
    public const string CadastrosEspecialidades = "cadastros.especialidades";
    public const string CadastrosConvenios = "cadastros.convenios";
    public const string RelatoriosVer = "relatorios.ver";
    public const string RelatoriosTodosMedicos = "relatorios.todos_medicos";
    public const string UsuariosGerenciar = "usuarios.gerenciar";

    /// <summary>Claim com cada transação liberada ao usuário (recalculada a cada requisição).</summary>
    public const string Claim = "transacao";

    public static readonly IReadOnlyList<Transacao> Todas =
    [
        new(DashboardVer, "Painel", "Dashboard", "Indicadores do dia e do mês, sala de espera e gráficos"),
        new(AgendaVer, "Agenda", "Ver agenda", "Agenda do dia/semana e horários livres"),
        new(AgendaTodosMedicos, "Agenda", "Agenda de todos os médicos", "Sem esta transação o usuário só vê a agenda do médico vinculado a ele"),
        new(AgendaAgendar, "Agenda", "Agendar e atender a agenda", "Agendar, remarcar, confirmar, registrar chegada e falta, cancelar"),
        new(AgendaBloqueios, "Agenda", "Bloqueios de agenda", "Incluir e excluir férias, feriados e consultório fechado"),
        new(PacientesVer, "Pacientes", "Consultar pacientes", "Pesquisar e ver o cadastro e as carteirinhas"),
        new(PacientesEditar, "Pacientes", "Cadastrar pacientes", "Incluir e alterar pacientes e carteirinhas"),
        new(ProntuarioVer, "Prontuário", "Ver prontuário", "Resumo clínico, evoluções, prescrições e receitas", Sensivel: true),
        new(ProntuarioEditar, "Prontuário", "Registrar no prontuário", "Resumo clínico, evoluções e prescrições (evolução e prescrição exigem usuário vinculado a um médico)", Sensivel: true),
        new(ProntuarioAnexos, "Prontuário", "Anexos do prontuário", "Ver, enviar e remover imagens e arquivos do prontuário", Sensivel: true),
        new(CadastrosMedicos, "Cadastros", "Médicos e horários", "Cadastro de médicos, vínculo com usuário e grade de horários"),
        new(CadastrosEspecialidades, "Cadastros", "Especialidades", "Cadastro de especialidades"),
        new(CadastrosConvenios, "Cadastros", "Convênios e planos", "Cadastro de convênios e planos"),
        new(RelatoriosVer, "Relatórios", "Relatórios", "Atendimentos e indicadores, com CSV"),
        new(RelatoriosTodosMedicos, "Relatórios", "Dashboard e relatórios de todos os médicos", "Sem esta transação o usuário só vê os números do médico vinculado a ele"),
        new(UsuariosGerenciar, "Administração", "Usuários da clínica", "Incluir e alterar usuários e escolher os perfis de cada um"),
    ];

    public static bool Existe(string codigo) => Todas.Any(t => t.Codigo == codigo);

    /// <summary>Transações dos perfis padrão, criados junto com o banco (o administrador do sistema pode alterá-las depois).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Padrao = new Dictionary<string, string[]>
    {
        [Perfis.Administrador] = Todas.Where(t => !t.Sensivel).Select(t => t.Codigo).ToArray(),
        [Perfis.Medico] = [DashboardVer, AgendaVer, AgendaAgendar, PacientesVer, PacientesEditar, ProntuarioVer, ProntuarioEditar, ProntuarioAnexos, RelatoriosVer],
        [Perfis.Recepcao] = [AgendaVer, AgendaTodosMedicos, AgendaAgendar, AgendaBloqueios, PacientesVer, PacientesEditar, CadastrosMedicos, CadastrosEspecialidades, CadastrosConvenios],
    };
}
