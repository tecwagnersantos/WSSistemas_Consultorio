/* Novo agendamento: busca de paciente, carteirinhas, especialidades do médico e horários livres. */
(function () {
    "use strict";
    const T = window.traduzir;
    const msg = texto => { const s = document.createElement("span"); s.className = "muted"; s.textContent = T(texto); return s.outerHTML; };
    const { api, toast } = window.App;
    const form = document.getElementById("form-agendamento");
    if (!form) return;

    const medicos = JSON.parse(form.dataset.medicos || "[]");
    const el = nome => form.elements[nome];
    const campoBusca = document.getElementById("busca-paciente");
    const listaSugestoes = form.querySelector(".sugestoes");
    const slots = document.getElementById("slots");

    // ---------- Paciente (autocompletar) ----------
    let temporizador = null, sugestoes = [], foco = -1;

    function mostrarSugestoes(itens) {
        sugestoes = itens; foco = -1;
        listaSugestoes.innerHTML = "";
        if (!itens.length) {
            listaSugestoes.innerHTML = '<li class="muted"></li>';
            listaSugestoes.firstChild.textContent = T("Nenhum paciente encontrado.");
        }
        itens.forEach((p, i) => {
            const li = document.createElement("li");
            li.innerHTML = `<strong></strong><small></small>`;
            li.querySelector("strong").textContent = p.nome;
            li.querySelector("small").textContent = [p.nascimento, p.cpf, p.celular].filter(Boolean).join(" · ");
            li.addEventListener("mousedown", ev => { ev.preventDefault(); escolherPaciente(i); });
            listaSugestoes.appendChild(li);
        });
        listaSugestoes.hidden = false;
    }

    async function escolherPaciente(i) {
        const p = sugestoes[i];
        if (!p) return;
        el("PacienteId").value = p.id;
        el("PacienteNome").value = p.nome;
        campoBusca.value = p.nome;
        listaSugestoes.hidden = true;
        await carregarCarteirinhas(p.id);
    }

    campoBusca.addEventListener("input", () => {
        el("PacienteId").value = "0";
        clearTimeout(temporizador);
        const termo = campoBusca.value.trim();
        if (termo.length < 2) { listaSugestoes.hidden = true; return; }
        temporizador = setTimeout(async () => {
            try { mostrarSugestoes(await api("/Pacientes/Buscar?termo=" + encodeURIComponent(termo))); }
            catch (err) { toast(err.message, "erro"); }
        }, 250);
    });
    campoBusca.addEventListener("keydown", e => {
        const itens = listaSugestoes.querySelectorAll("li");
        if (listaSugestoes.hidden || !sugestoes.length) return;
        if (e.key === "ArrowDown" || e.key === "ArrowUp") {
            e.preventDefault();
            foco = (foco + (e.key === "ArrowDown" ? 1 : -1) + itens.length) % itens.length;
            itens.forEach((li, i) => li.classList.toggle("foco", i === foco));
        } else if (e.key === "Enter" && foco >= 0) {
            e.preventDefault();
            escolherPaciente(foco);
        } else if (e.key === "Escape") {
            listaSugestoes.hidden = true;
        }
    });
    campoBusca.addEventListener("blur", () => setTimeout(() => { listaSugestoes.hidden = true; }, 150));

    async function carregarCarteirinhas(pacienteId) {
        const select = el("PacientePlanoId");
        const selecionado = select.value || form.dataset.plano;
        select.innerHTML = "";
        select.add(new Option(T("Particular"), ""));
        if (!pacienteId || pacienteId === "0") return;
        try {
            const lista = await api(`/Pacientes/Carteirinhas/${pacienteId}`);
            lista.forEach(c => select.add(new Option(c.descricao, c.id, false, String(c.id) === String(selecionado))));
        } catch (err) { toast(err.message, "erro"); }
    }

    // ---------- Médico, especialidade e duração ----------
    function atualizarEspecialidades() {
        const medico = medicos.find(m => String(m.id) === el("MedicoId").value);
        const select = el("EspecialidadeId");
        const atual = select.value !== "0" ? select.value : form.dataset.especialidade;
        select.innerHTML = "";
        if (!medico) { select.add(new Option(T("Selecione o médico"), "0")); return; }
        if (!medico.especialidades.length) select.add(new Option(T("Médico sem especialidade cadastrada"), "0"));
        medico.especialidades.forEach(e => select.add(new Option(e.nome, e.id, false,
            String(e.id) === String(atual) || (!atual || atual === "0") && e.principal)));
        el("DuracaoMin").placeholder = T("padrão: {0} min", medico.duracao);
    }

    // ---------- Horários livres ----------
    async function carregarSlots() {
        const medicoId = el("MedicoId").value, dia = el("Dia").value;
        if (!medicoId || medicoId === "0" || !dia) {
            slots.innerHTML = msg("Selecione médico e dia.");
            return;
        }
        slots.innerHTML = msg("Carregando...");
        try {
            const livres = await api(`/Agenda/HorariosLivres?medicoId=${medicoId}&dia=${dia}`);
            slots.innerHTML = "";
            if (!livres.length) {
                slots.innerHTML = msg("Nenhum horário livre na grade deste dia. Para encaixe, digite a hora.");
                return;
            }
            livres.forEach(s => {
                const b = document.createElement("button");
                b.type = "button"; b.className = "slot"; b.textContent = s.inicio;
                if (el("Hora").value === s.inicio) b.classList.add("escolhido");
                b.addEventListener("click", () => {
                    el("Hora").value = s.inicio;
                    slots.querySelectorAll(".slot").forEach(x => x.classList.toggle("escolhido", x === b));
                });
                slots.appendChild(b);
            });
        } catch (err) {
            slots.innerHTML = "";
            toast(err.message, "erro");
        }
    }

    el("MedicoId").addEventListener("change", () => { atualizarEspecialidades(); carregarSlots(); });
    el("Dia").addEventListener("change", carregarSlots);
    el("Hora").addEventListener("input", () => slots.querySelectorAll(".slot").forEach(x => x.classList.toggle("escolhido", x.textContent === el("Hora").value)));

    form.addEventListener("submit", e => {
        if (!el("PacienteId").value || el("PacienteId").value === "0") {
            e.preventDefault();
            toast(T("Selecione o paciente na lista de sugestões."), "erro");
            campoBusca.focus();
        }
    });

    atualizarEspecialidades();
    carregarSlots();
    carregarCarteirinhas(el("PacienteId").value);
})();
