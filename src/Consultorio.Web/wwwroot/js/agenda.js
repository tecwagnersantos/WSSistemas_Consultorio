/* Agenda: mudança de status, cancelamento com motivo e remarcação (sem recarregar formulários). */
(function () {
    "use strict";
    const T = window.traduzir;
    const { api, toast } = window.App;

    const dlgRemarcar = document.getElementById("dlg-remarcar");
    const dlgMotivo = document.getElementById("dlg-motivo");
    let linhaAtual = null;

    async function alterarStatus(id, status, observacao) {
        await api(`/Agenda/AlterarStatus/${id}`, { method: "POST", body: { status, observacao: observacao || null } });
        sessionStorage.setItem("agenda-msg", T("Status atualizado."));
        location.reload();
    }

    document.addEventListener("click", async e => {
        const botao = e.target.closest("button[data-status], button[data-remarcar]");
        if (!botao) return;
        const linha = botao.closest("tr[data-id]");
        if (!linha) return;
        botao.closest("details")?.removeAttribute("open");
        linhaAtual = linha;

        if (botao.hasAttribute("data-remarcar")) {
            const f = dlgRemarcar.querySelector("form");
            f.reset();
            dlgRemarcar.querySelector("[data-paciente-nome]").textContent = linha.dataset.paciente;
            f.elements.novoInicio.value = linha.dataset.inicio;
            f.elements.duracaoMin.value = linha.dataset.duracao;
            dlgRemarcar.showModal();
            return;
        }

        const status = botao.dataset.status;
        if (botao.hasAttribute("data-pedir-motivo")) {
            dlgMotivo.querySelector("form").reset();
            dlgMotivo.querySelector("[data-paciente-nome]").textContent = linha.dataset.paciente;
            dlgMotivo.showModal();
            return;
        }
        if (status === "Faltou" && !confirm(T("Marcar falta de {0}?", linha.dataset.paciente))) return;

        botao.disabled = true;
        try { await alterarStatus(linha.dataset.id, status); }
        catch (err) { toast(err.message, "erro"); botao.disabled = false; }
    });

    dlgMotivo?.querySelector("form").addEventListener("submit", async e => {
        e.preventDefault();
        const obs = e.target.elements.observacao.value.trim();
        try { await alterarStatus(linhaAtual.dataset.id, "Cancelado", obs); dlgMotivo.close(); }
        catch (err) { toast(err.message, "erro"); }
    });

    dlgRemarcar?.querySelector("form").addEventListener("submit", async e => {
        e.preventDefault();
        const f = e.target.elements;
        try {
            await api(`/Agenda/Remarcar/${linhaAtual.dataset.id}`, {
                method: "POST",
                body: {
                    novoInicio: f.novoInicio.value.length === 16 ? f.novoInicio.value + ":00" : f.novoInicio.value,
                    duracaoMin: f.duracaoMin.value ? parseInt(f.duracaoMin.value, 10) : null,
                    observacao: f.observacao.value || null
                }
            });
            dlgRemarcar.close();
            sessionStorage.setItem("agenda-msg", T("Agendamento remarcado."));
            location.reload();
        } catch (err) { toast(err.message, "erro"); }
    });

    const msg = sessionStorage.getItem("agenda-msg");
    if (msg) { sessionStorage.removeItem("agenda-msg"); toast(msg); }
})();
