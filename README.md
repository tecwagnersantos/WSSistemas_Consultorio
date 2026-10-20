# Sistema de Consultório Médico (ASP.NET Core MVC)

Agendamento, prontuário eletrônico, dashboard e relatórios em **um único projeto web ASP.NET**:
C# no servidor, páginas Razor, **CSS e JavaScript puros** (sem Angular, sem jQuery, sem Bootstrap) e banco **SQL Server**.
Roda no Visual Studio 2022 com F5 e publica no IIS como um site só.

**Multi-clínica (por cliente):** um único site e um único banco atendem vários clientes (consultórios/clínicas).
Cada usuário pertence a uma clínica e só enxerga os dados dela. A equipe do sistema usa a **área de administração**
(`/Admin`), com login separado, para manter os clientes, definir limites de médicos e usuários e acompanhar dashboards e relatórios.
Veja [Clínicas (clientes)](#clínicas-clientes) e [Área de administração do sistema](#área-de-administração-do-sistema-admin).

**Multi-idioma:** português (padrão), inglês, espanhol e francês, escolhidos por cada usuário. Veja [Idiomas](#idiomas).

```
Consultorio/
├── Consultorio.sln                    Abra este arquivo no Visual Studio
├── src/
│   ├── Consultorio.Domain             Entidades e regras (fluxo de status da agenda, assinatura da evolução)
│   ├── Consultorio.Application        Serviços de caso de uso (agenda, pacientes, prontuário, cadastros, login)
│   ├── Consultorio.Infrastructure     EF Core (SQL Server), Dapper (relatórios), senha (PBKDF2), auditoria LGPD
│   └── Consultorio.Web                O site: Controllers, Views (Razor), wwwroot/css e wwwroot/js
│                                      Areas/Admin = área de administração do sistema (login separado)
├── database/
│   ├── 01_schema.sql                  Cria tabelas (com ClinicaId), views e dados de domínio (perfis, status)
│   ├── 02_dados_exemplo.sql           Opcional: "Clínica Exemplo" com médico, convênio, grade e pacientes
│   ├── 03_atualiza_v0.7_situacao_cliente.sql  Só para banco já criado com o schema v0.6
│   ├── 04_atualiza_v0.8_pagamentos.sql        Só para banco já criado com o schema v0.7
│   ├── 05_atualiza_v0.9_idiomas.sql           Só para banco já criado com o schema v0.8
│   └── 06_atualiza_v1.0_documentos.sql        Só para banco já criado com o schema v0.9
└── docs/
    └── decisoes-arquitetura.md
```

## O que o sistema faz

A coluna *Quem acessa* mostra os **perfis padrão**. O que cada perfil pode ver e fazer é configurável na área `/Admin → Perfis de acesso` (veja abaixo).

| Tela | Quem acessa (padrão) | Recursos |
|------|-------------|----------|
| **Login** | todos | Cookie de sessão, bloqueio após 5 senhas erradas, limite de tentativas por IP. O e-mail identifica a clínica |
| **Dashboard** | Administrador, Médico | Agendados/atendidos hoje, sala de espera, faturamento e faltas do mês, espera média, gráfico de 30 dias, atendimentos por convênio |
| **Agenda** | todos (médico vê só a própria) | Visão dia/semana, filtro por médico, confirmar, registrar chegada, falta, cancelar com motivo, remarcar, botão **Atender** para o médico |
| **Novo agendamento** | todos | Busca de paciente por nome/CPF/celular, carteirinha do convênio, horários livres pela grade do médico, encaixe, valor |
| **Bloqueios** | todos (incluir: Admin/Recepção) | Férias, feriados, consultório fechado |
| **Pacientes** | todos | Cadastro completo, busca de endereço pelo CEP, carteirinhas, consentimento LGPD |
| **Prontuário** | **somente Médico** | Resumo clínico (alergias em destaque), evoluções com sinais vitais e IMC, rascunho/assinatura (imutável, com hash SHA-256), retificação, prescrições, **receita para impressão** e **anexos** (fotos, exames, laudos, termos: imagens, PDF e documentos, vários por vez, com miniatura e ligação opcional a uma evolução) |
| **Cadastros** | Administrador, Recepção | Médicos (especialidades, RQE, vínculo com usuário), grade de horários, especialidades, convênios e planos |
| **Relatórios** | Administrador (todos), Médico (os próprios) | Atendimentos por período/médico/status e indicadores mensais, com **exportação CSV** (abre no Excel) e impressão |
| **Usuários** | Administrador (da clínica) | Inclusão, perfis, ativação, troca de senha — só usuários da própria clínica |

Toda leitura e gravação de prontuário e cadastro de paciente é registrada em `seguranca.LogAuditoria`.

## Pré-requisitos

- **Visual Studio 2022 versão 17.12 ou mais nova**, com a carga de trabalho **"ASP.NET e desenvolvimento Web"** (já instala o .NET 9 SDK).
  Confira em *Ajuda → Sobre*; para atualizar, *Ajuda → Verificar Atualizações*.
- **SQL Server 2019 ou mais novo** (Express ou Developer servem) e o **SQL Server Management Studio (SSMS)**.
- Não é preciso Node.js nem npm.

## Executar no Visual Studio 2022 (passo a passo)

1. **Crie o banco.** No SSMS, conecte no seu SQL Server e rode:
   ```sql
   CREATE DATABASE Consultorio COLLATE Latin1_General_100_CI_AI_SC_UTF8;
   ```
   Depois abra `database/01_schema.sql`, selecione o banco **Consultorio** na barra do SSMS e execute (F5).
   Se quiser dados para testar, execute também `database/02_dados_exemplo.sql`.

2. **Abra a solução.** No Visual Studio: *Arquivo → Abrir → Projeto/Solução* → `Consultorio.sln`.

3. **Ajuste a connection string** em `src/Consultorio.Web/appsettings.json` (`ConnectionStrings:Consultorio`). Exemplos:
   - SQL Express, login do Windows: `Server=.\SQLEXPRESS;Database=Consultorio;Trusted_Connection=True;TrustServerCertificate=True`
   - SQL Server padrão, login do Windows: `Server=localhost;Database=Consultorio;Trusted_Connection=True;TrustServerCertificate=True`
   - Usuário SQL: `Server=localhost;Database=Consultorio;User Id=sa;Password=SUA_SENHA;TrustServerCertificate=True`

4. **Rode.** No Gerenciador de Soluções, clique com o botão direito em **Consultorio.Web** → *Definir como Projeto de Inicialização*.
   Escolha o perfil **https** na barra de ferramentas e pressione **F5**. Se o Visual Studio pedir para confiar no certificado de desenvolvimento, aceite.
   O navegador abre em https://localhost:7001.

5. **Entre** com `admin@consultorio.local` / `Admin@12345`.
   **na área de administração: https://localhost:7001/Admin**.
   Esse é o **Administrador do sistema**, criado automaticamente na primeira execução em modo Development, quando ainda não há administradores
   (configuração em `appsettings.Development.json`). Troque a senha em **Alterar senha** logo no primeiro acesso.
   Em **Clientes → Novo cliente**, cadastre a primeira clínica com o administrador dela; depois entre na tela normal (https://localhost:7001)
   com o e-mail desse administrador da clínica.

## Clínicas (clientes)

- **Banco único, multi-cliente.** Todas as tabelas de dados têm a coluna `ClinicaId`. O banco impede que um registro aponte para dados de
  outra clínica (chaves estrangeiras compostas por `ClinicaId`), e CPF do paciente, CRM do médico e nomes de convênio/especialidade são únicos *por clínica*.
- **Isolamento na aplicação.** No login, a clínica do usuário vai para o cookie (`clinica_id`). O `AppDbContext` aplica um filtro global
  `ClinicaId = clínica do usuário` em todas as consultas do EF Core e preenche `ClinicaId` sozinho nas inclusões; os relatórios (Dapper)
  filtram por `ClinicaId` explicitamente. Ao criar consultas novas com Dapper/SQL, **sempre** filtre por `ClinicaId`.
- **Perfis de acesso.** Cada usuário da clínica tem um ou mais perfis e pode usar a soma das **transações** (telas e ações) liberadas por eles.
  Vêm prontos os perfis padrão *Administrador*, *Médico* e *Recepção*. Os administradores do sistema não são usuários de clínica
  (ficam em `seguranca.AdministradorSistema` e entram por `/Admin`).
- **Login.** O e-mail é único no sistema inteiro e define a clínica; não há tela de escolha de clínica. Um médico que atende em duas
  clínicas precisa de um e-mail (usuário) em cada uma.
- **Limites do plano.** Cada cliente pode ter um máximo de **médicos ativos** e de **usuários ativos** (em branco = sem limite).
  A clínica não consegue incluir nem reativar além do limite; as telas *Usuários* e *Médicos* mostram o uso. Inativar libera a vaga.
- **Período de acesso (teste/implantação).** Todo cliente novo começa em teste, com acesso até uma data (padrão: 30 dias a partir do
  cadastro). A clínica entra até essa data, inclusive; no dia seguinte o login é recusado ("O período de acesso desta clínica terminou...")
  e quem estiver logado perde o acesso na próxima página. Nos últimos 15 dias a clínica vê um aviso no topo das páginas.
  Para **prorrogar**, altere a data em **/Admin → Clientes → Editar**. Para encerrar o teste, use **Liberar versão definitiva** na página
  do cliente (ou marque *Versão definitiva liberada* no cadastro): a data deixa de valer.
- **Pagamentos dos clientes.** No cadastro do cliente (*Editar cliente*, quadro **Cobrança**) defina o valor da mensalidade, o dia do
  vencimento (1 a 28), a tolerância em dias e se o sistema bloqueia sozinho por atraso. O site gera a mensalidade de cada mês no início
  do mês (ou use **/Admin → Pagamentos → Gerar mensalidades do mês**); cobranças avulsas (implantação, treinamento) são criadas na página
  do cliente. Em **/Admin → Pagamentos** você vê pendentes, atrasadas, pagas e canceladas, com totais do mês e exportação CSV, e em cada
  cobrança: **Registrar pagamento** (data, valor, forma: Pix, boleto, cartão, transferência, dinheiro), **Prorrogar**, **Cancelar** e
  **Estornar**. Nos 5 dias antes do vencimento e depois dele, quem administra a clínica vê um aviso no topo das páginas. Passada a
  tolerância sem pagamento, a clínica é **bloqueada automaticamente** (motivo "Pagamento em atraso") e ao registrar o pagamento ou
  prorrogar o vencimento ela volta na hora. Bloqueio feito à mão pelo suporte não é desfeito pelo pagamento. Tudo fica na auditoria.
- **Situação do cliente: Ativo, Bloqueado ou Inativo.** Na página do cliente em **/Admin → Clientes**, use *Bloquear (temporário)*
  (ex.: mensalidade em atraso) ou *Inativar (deixou de ser cliente)*, sempre com o motivo; também dá para mudar em *Editar cliente*.
  Bloqueado ou inativo, ninguém da clínica entra (a tela de login diz se está bloqueada ou inativa) e quem estiver logado perde o acesso
  na próxima página. Os dados continuam guardados. **Reativar cliente** devolve o acesso na hora. A lista de clientes mostra por padrão
  os ativos e bloqueados; os inativos aparecem no filtro *Inativos* ou *Todos*. Toda mudança de situação fica na auditoria.
- O nome da clínica aparece no menu e no cabeçalho da receita (endereço, cidade e telefone vêm do cadastro da clínica).

## Área de administração do sistema (/Admin)

Endereço: `https://seu-servidor/Admin`. **Login, cookie e tabela de usuários separados** das clínicas: o login de uma clínica não abre
a área admin, e o administrador do sistema não entra nas telas das clínicas. A sessão expira com 30 minutos sem uso.

| Tela | O que faz |
|------|-----------|
| **Dashboard** | Recebido no mês e pagamentos em atraso (com a lista das cobranças atrasadas), clientes ativos/bloqueados/inativos/em teste (com a lista dos bloqueados e o motivo), clientes com período de acesso vencendo (15 dias) ou vencido, usuários e médicos ativos, pacientes, agendamentos e atendimentos do mês, acessos dos últimos 7 dias, gráfico de agendamentos de 30 dias (todos os clientes), clientes com mais agendamentos e clientes no limite do plano |
| **Pagamentos** | Mensalidades e cobranças avulsas de todos os clientes, filtros por situação, cliente e vencimento, totais do mês (previsto, recebido, em aberto, em atraso), gerar mensalidades, registrar pagamento, prorrogar, cancelar, estornar, CSV |
| **Clientes** | Cadastro da clínica, **cobrança** (mensalidade, vencimento, tolerância, bloqueio automático), **período de acesso** (data final do teste / versão definitiva, com filtro *Em teste*, *Teste vencido* e *Definitivo*), **limites de médicos e usuários**, observações internas, **situação (ativo, bloqueado, inativo) com motivo**. Na página do cliente: uso do plano, usuários (criar, inativar/reativar, **redefinir senha e desbloquear**) e médicos |
| **Relatórios** | Clientes e uso do plano · Médicos (por cliente, situação, agendamentos e atendidos no período) · Usuários (por cliente, perfil, situação, último acesso) · Agendamentos por cliente (total, em aberto, atendidos, faltas, taxa de faltas, cancelados, remarcados). Todos com **CSV** e impressão |
| **Perfis de acesso** | Cria e altera perfis e marca as **transações** de cada um (o que o usuário pode ver e fazer). Perfil *padrão* vale para todos os clientes; perfil *exclusivo* só aparece para os usuários de um cliente. Os perfis de cada usuário são trocados na página do cliente (botão **Perfis**) ou pelo administrador da clínica em *Usuários* |
| **Administradores** | Quem tem acesso à área admin (inclusão, troca de senha, desativação) |

### Transações (o que um perfil pode liberar)

| Grupo | Transação | O que libera |
|-------|-----------|--------------|
| Painel | **Dashboard** (`dashboard.ver`) | Indicadores do dia e do mês, sala de espera e gráficos |
| Agenda | **Ver agenda** (`agenda.ver`) | Agenda do dia/semana e horários livres |
| Agenda | **Agenda de todos os médicos** (`agenda.todos_medicos`) | Sem esta transação o usuário só vê a agenda do médico vinculado a ele |
| Agenda | **Agendar e atender a agenda** (`agenda.agendar`) | Agendar, remarcar, confirmar, registrar chegada e falta, cancelar |
| Agenda | **Bloqueios de agenda** (`agenda.bloqueios`) | Incluir e excluir férias, feriados e consultório fechado |
| Pacientes | **Consultar pacientes** (`pacientes.ver`) | Pesquisar e ver o cadastro e as carteirinhas |
| Pacientes | **Cadastrar pacientes** (`pacientes.editar`) | Incluir e alterar pacientes e carteirinhas |
| Prontuário | **Ver prontuário** (`prontuario.ver`) | Resumo clínico, evoluções, prescrições e receitas — dado de saúde |
| Prontuário | **Registrar no prontuário** (`prontuario.editar`) | Resumo clínico, evoluções e prescrições (evolução e prescrição exigem usuário vinculado a um médico) — dado de saúde |
| Prontuário | **Anexos do prontuário** (`prontuario.anexos`) | Ver, enviar e remover imagens e arquivos do prontuário — dado de saúde |
| Cadastros | **Médicos e horários** (`cadastros.medicos`) | Cadastro de médicos, vínculo com usuário e grade de horários |
| Cadastros | **Especialidades** (`cadastros.especialidades`) | Cadastro de especialidades |
| Cadastros | **Convênios e planos** (`cadastros.convenios`) | Cadastro de convênios e planos |
| Relatórios | **Relatórios** (`relatorios.ver`) | Atendimentos e indicadores, com CSV |
| Relatórios | **Dashboard e relatórios de todos os médicos** (`relatorios.todos_medicos`) | Sem esta transação o usuário só vê os números do médico vinculado a ele |
| Administração | **Usuários da clínica** (`usuarios.gerenciar`) | Incluir e alterar usuários e escolher os perfis de cada um |

Sem *Agenda de todos os médicos* / *Dashboard e relatórios de todos os médicos*, o usuário só vê os dados do médico vinculado a ele
(*Cadastros → Médicos → Usuário de acesso*). As transações de **prontuário** são dados de saúde (LGPD): por padrão só o perfil *Médico* as tem;
se liberar para outro perfil (ex.: enfermagem), todo acesso continua na auditoria. Registrar evolução e prescrição exige usuário vinculado a um médico.
Mudanças em perfis valem na próxima página que o usuário abrir, sem precisar sair e entrar. Menu e botões aparecem conforme as transações.
Para criar uma tela nova no código: inclua a transação em `Consultorio.Domain/Common/Transacoes.cs` (o site a cadastra no banco ao iniciar)
e proteja o controller/ação com `[Authorize(Policy = Transacoes.Xxx)]`.

A área admin mostra só contagens e cadastros: não abre pacientes, agenda nem prontuário das clínicas (LGPD).
As ações feitas nela ficam em `seguranca.LogAuditoria` com `AdministradorSistemaId`.

## Primeiros passos dentro da clínica

Entrando com o administrador da clínica:

1. **Cadastros → Especialidades**: cadastre as especialidades atendidas.
2. **Administração → Usuários**: crie os usuários. Para cada médico, um usuário com perfil **Médico**; para a recepção, perfil **Recepção**
   (ou outro perfil criado pelo suporte em */Admin → Perfis de acesso*).
3. **Cadastros → Médicos**: cadastre o médico, marque as especialidades e escolha o **usuário de acesso** (é esse vínculo que libera a agenda própria e o prontuário).
4. **Médicos → Horários**: monte a grade (dias, horário e intervalo). A tela de agendamento mostra os horários livres a partir dela.
5. **Cadastros → Convênios**: cadastre convênios e planos (opcional; sem convênio o atendimento é particular).
6. **Pacientes → Novo paciente → Salvar e agendar.**

Fluxo do atendimento: Recepção agenda → confirma → marca **Chegou** (sala de espera) → médico clica **Atender** na agenda →
registra a evolução → **Salvar e assinar** (o agendamento passa para *Atendido* automaticamente) → emite a prescrição e imprime a receita.

## Idiomas

O sistema fala **português (pt-BR, padrão), inglês (en-US), espanhol (es-ES) e francês (fr-FR)**.

- **Escolha:** o seletor de idioma fica no canto da tela de login, no menu lateral das clínicas e no topo da área `/Admin`.
  A escolha vale na hora, fica guardada no navegador (cookie de 1 ano) e, com o usuário logado, também no cadastro dele
  (`seguranca.Usuario.Idioma` / `seguranca.AdministradorSistema.Idioma`): ao entrar em outro computador o sistema já abre no idioma dele.
  Quem nunca escolheu vê em português. O endereço também aceita `?culture=en-US` (útil para testar).
- **Onde ficam os textos:** `src/Consultorio.Web/Resources/Textos.resx` (português) e `Textos.en.resx`, `Textos.es.resx`, `Textos.fr.resx`.
  A **chave é o próprio texto em português**; o valor é a tradução. Abra no Visual Studio (editor de recursos) ou em qualquer editor de XML.
  Texto sem tradução aparece em português, então nada quebra se faltar uma linha.
  Não crie duas chaves que só mudam em maiúscula/minúscula (ex.: "Até" e "até"): o .NET trata como a mesma e uma delas fica sem tradução.
- **Como usar no código:** nas views, `@T["Texto em português"]` (ou `@T["Agendamento criado para {0}.", data]`);
  nos controllers, `T["..."]` e `Sucesso("...")`, que já traduz; nas regras de negócio, `throw new DomainException("Texto {0}.", valor)`:
  a mensagem é traduzida ao chegar na tela. No JavaScript, `traduzir("Texto")` com as chaves listadas em `Views/Shared/_TextosJs.cshtml`.
- **Datas e números** seguem o idioma (14/10/2026 em português e espanhol, 10/14/2026 em inglês). **A moeda continua a mesma**
  (`Regional:SimboloMoeda`, padrão `R$`), só muda a pontuação.
- **Ficam no idioma em que foram digitados:** os dados cadastrados (nomes, observações, evoluções, descrições de cobranças e motivos)
  e os nomes de perfis criados pelo suporte.
- **Incluir outro idioma:** copie `Textos.en.resx` para `Textos.<código>.resx`, traduza os valores e acrescente o idioma em
  `Idiomas.Todos` (`src/Consultorio.Web/Textos.cs`).

## Configurações (`appsettings.json`)

| Chave | Para que serve |
|-------|----------------|
| `ConnectionStrings:Consultorio` | Banco de dados |
| `Consultorio:Nome` | Nome do sistema na tela de login (dentro do sistema aparece o nome da clínica do usuário) |
| `Sessao:MinutosInatividade` | Tempo sem uso até pedir login de novo nas clínicas (padrão 60) |
| `Sessao:MinutosInatividadeAdmin` | O mesmo para a área `/Admin` (padrão 30) |
| `Anexos:Pasta` | Pasta onde ficam os arquivos anexados ao prontuário (padrão `App_Data/Anexos`, dentro da pasta do site). **Em produção use um caminho fora da pasta do site**, ex.: `D:\ConsultorioAnexos` (no JSON, com barra dupla: `"D:\\ConsultorioAnexos"`) |
| `Anexos:TamanhoMaximoMB` | Tamanho máximo de cada arquivo anexado (padrão 20). Um envio pode ter até 10 arquivos e 100 MB no total (limite também no `web.config`) |
| `Regional:SimboloMoeda` | Símbolo da moeda mostrado em todos os idiomas (padrão `R$`) |
| `Demonstracao:Habilitado` / `Dias` / `MaxMedicos` / `MaxUsuarios` | Cadastro de demonstração na tela de login ("Experimente grátis"): cria a clínica em período de teste por `Dias` dias, com esses limites, e o usuário administrador (e médico, se informar o CRM). Vencido o prazo, o login é recusado até o suporte liberar a versão definitiva ou estender o prazo em `/Admin → Clientes`. `Habilitado = false` esconde o link |
| `Bootstrap:AdminEmail` / `AdminSenha` | Cria o primeiro Administrador do sistema (área `/Admin`) se não houver nenhum. Deixe vazio em produção depois do primeiro acesso |

## Publicar no IIS (servidor próprio)

1. No servidor, instale o **ASP.NET Core 9.0 Hosting Bundle** (https://dotnet.microsoft.com/download/dotnet/9.0 → *Hosting Bundle*) e reinicie o IIS (`iisreset`).
2. No Visual Studio: botão direito em **Consultorio.Web** → *Publicar* → *Pasta* → Publicar. (Ou, no terminal: `dotnet publish src/Consultorio.Web -c Release -o publicar`.)
3. Copie a pasta publicada para o servidor (ex.: `C:\inetpub\consultorio`).
4. No Gerenciador do IIS: crie um **Pool de Aplicativos** com *Versão do .NET CLR* = **Sem Código Gerenciado**; crie o **Site** (ou Aplicativo) apontando para a pasta, usando esse pool e um certificado HTTPS.
5. Configure a connection string de produção: edite `appsettings.json` na pasta publicada, ou crie `appsettings.Production.json`, ou defina a variável `ConnectionStrings__Consultorio` no `web.config`.
6. Se usar login do Windows no SQL Server, dê acesso ao banco para a identidade do pool (`IIS APPPOOL\NomeDoPool`).
7. Para criar o Administrador do sistema em produção, preencha `Bootstrap:AdminEmail`/`AdminSenha`, acesse o site uma vez, entre em `/Admin`, troque a senha e apague essas chaves.
   Para mais segurança, a área `/Admin` pode ser restrita por IP no IIS (*Restrições de Endereço IP e Domínio* no caminho `/Admin`).

**Anexos do prontuário no servidor:** crie a pasta (ex.: `D:\ConsultorioAnexos`), informe-a em `Anexos:Pasta` e dê permissão de
**Modificar** nela para a identidade do pool (`IIS APPPOOL\NomeDoPool`). Os arquivos ficam em `{pasta}\{id da clínica}\{ano}\{mês}\` com nome
gerado (o nome original fica só no banco). **Inclua essa pasta no backup**, junto com o banco: o banco sem a pasta perde os arquivos, e a pasta
sem o banco não diz de quem é cada arquivo.

O site também funciona publicado em um diretório virtual (ex.: `https://servidor/consultorio`).

## Problemas comuns

- **"Cannot open database" ou erro de certificado ao conectar**: confira a connection string e se o `01_schema.sql` rodou no banco certo. Mantenha `TrustServerCertificate=True` em desenvolvimento.
- **"O acesso desta clínica está bloqueado"** ou **"Esta clínica está inativa"**: o Administrador do sistema reativa em */Admin → Clientes* (filtro *Bloqueados* ou *Inativos*) → **Reativar cliente**.
- **"O acesso desta clínica está bloqueado por pagamento em atraso"**: registre o pagamento em */Admin → Pagamentos* (ou prorrogue o vencimento); a clínica volta na hora.
- **Mensalidades não foram geradas sozinhas**: a rotina roda ao iniciar o site e de hora em hora, e só gera mensalidade ainda não vencida. Se o site ficou parado no IIS, use *Gerar mensalidades do mês*. Para desligar a rotina: `Cobrancas:RotinaAutomatica = false` no `appsettings.json`. Para o site não "dormir", configure no IIS o pool com *Start Mode = AlwaysRunning* e *Idle Time-out = 0*.
- **Erro 429 no login**: mais de 10 tentativas por minuto do mesmo IP. O limite fica em `Seguranca:LoginsPorMinuto` no `appsettings.json`.
- **"O período de acesso desta clínica terminou em ..."**: o teste venceu; prorrogue a data ou libere a versão definitiva em */Admin → Clientes*.
- **Clínica de exemplo não entra depois de 30 dias**: o `02_dados_exemplo.sql` cria a *Clínica Exemplo* em teste por 30 dias; libere a versão definitiva em */Admin*.
- **Administrador do sistema não entra na tela normal**: é o esperado; ele entra em `/Admin`. Para operar uma clínica, use um usuário dela
  (o suporte pode redefinir a senha em */Admin → Clientes → usuário → Redefinir senha*).
- **"O plano desta clínica permite N usuário(s)/médico(s)"**: limite atingido; inative alguém ou aumente o limite em */Admin → Clientes → Editar*.
- **Login não aceita `admin@consultorio.local`**: ele entra em `/Admin`, não na tela das clínicas. Além disso, o administrador só é criado se a tabela `seguranca.AdministradorSistema` estiver vazia e o site rodar em modo Development (perfil **https**). Veja a janela *Saída* do Visual Studio: se aparecer "Não foi possível criar o administrador do sistema", é a connection string.
- **Erro ao anexar arquivo ("Access to the path ... is denied")**: a identidade do pool do IIS não tem permissão de Modificar na pasta de `Anexos:Pasta`.
- **"O arquivo deste anexo não foi encontrado na pasta de anexos"**: a pasta mudou ou não foi restaurada do backup; confira `Anexos:Pasta`.
- **Envio de anexo grande falha com erro 404.13 ou 413**: o envio passou de 100 MB; mande menos arquivos por vez (ou aumente `maxAllowedContentLength` no `web.config` e o `LimiteEnvio` do `ProntuarioController`).
- **Tela de agendamento sem horários livres**: o médico precisa de grade em *Cadastros → Médicos → Horários*, e o dia não pode estar bloqueado. Para encaixe, digite a hora manualmente.
- **"Acesso negado" ou item sumiu do menu**: os perfis do usuário não liberam aquela transação. Confira em */Admin → Perfis de acesso* e nos perfis do usuário (*/Admin → Clientes → usuário → Perfis*).
- **Médico não vê o prontuário / não consegue registrar evolução**: o usuário precisa de um perfil com as transações de prontuário (padrão: **Médico**) e estar vinculado ao cadastro do médico (*Cadastros → Médicos → Usuário de acesso*). Depois de vincular, saia e entre de novo.
- **Projeto não abre ou não compila**: atualize o Visual Studio para 17.12+ (precisa do .NET 9 SDK).
- **HTTP Error 500.31/500.19 no IIS**: falta o Hosting Bundle do .NET 9 no servidor, ou o IIS não foi reiniciado depois da instalação.

## Observações técnicas

- O banco é criado pelos scripts SQL; o EF Core só mapeia as tabelas (não use `dotnet ef database update`).
- O `01_schema.sql` (v1.0, multi-clínica) é para banco **novo**. Banco já criado com o v0.6: rode uma vez, em ordem, o
  `03_atualiza_v0.7_situacao_cliente.sql` (clientes suspensos viram *Bloqueado*), o `04_atualiza_v0.8_pagamentos.sql`, o `05_atualiza_v0.9_idiomas.sql` e o `06_atualiza_v1.0_documentos.sql` (atestados, declarações, exames, encaminhamentos).
  Banco criado com o v0.7: rode o `04`, o `05` e o `06`. Banco criado com o v0.8: rode o `05` e o `06`. Banco criado com o v0.9: rode só o `06`. Se você já criou o banco com a versão anterior (clínica única), peça o script de migração.
- Para ler dados de todas as clínicas use `IgnoreQueryFilters()` de propósito, e só em código da área admin (`Consultorio.Application/Admin`).
- Formulários protegidos contra CSRF (antiforgery) em todos os POST; cookies `HttpOnly`.
- As chamadas JavaScript (status da agenda, horários livres, busca de paciente) usam `fetch` para endpoints JSON nos próprios controllers.
- A busca de endereço pelo CEP consulta o ViaCEP direto do navegador; sem internet, basta digitar o endereço.
- **.NET 9 tem suporte da Microsoft até 10/11/2026.** Para migrar ao .NET 10 (LTS), troque `net9.0` por `net10.0` em `Directory.Build.props`, atualize os pacotes `Microsoft.EntityFrameworkCore.*` e `Microsoft.Extensions.Identity.Core` para 10.x e instale o Hosting Bundle 10 no servidor.
