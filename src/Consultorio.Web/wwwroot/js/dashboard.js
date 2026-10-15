/* Gráfico de barras empilhadas (últimos 30 dias) desenhado em SVG puro. */
(function () {
    "use strict";
    const T = window.traduzir;
    const area = document.getElementById("grafico-30dias");
    if (!area) return;
    const dados = JSON.parse(area.dataset.serie || "[]");

    // Completa os dias sem movimento para o eixo ficar contínuo
    const hoje = new Date(); hoje.setHours(0, 0, 0, 0);
    const porData = Object.fromEntries(dados.map(d => [d.data, d]));
    const dias = [];
    for (let i = 29; i >= 0; i--) {
        const d = new Date(hoje); d.setDate(hoje.getDate() - i);
        const chave = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
        const v = porData[chave] || {};
        dias.push({ data: d, atendidos: v.Atendidos || 0, faltas: v.Faltas || 0, cancelados: v.Cancelados || 0 });
    }

    const L = 600, A = 240, mE = 28, mB = 22, mT = 8;
    const max = Math.max(1, ...dias.map(d => d.atendidos + d.faltas + d.cancelados));
    const passo = (L - mE) / dias.length, larg = passo * 0.7;
    const y = v => A - mB - (v / max) * (A - mB - mT);
    const ns = "http://www.w3.org/2000/svg";
    const svg = document.createElementNS(ns, "svg");
    svg.setAttribute("viewBox", `0 0 ${L} ${A}`);
    svg.setAttribute("preserveAspectRatio", "none");
    svg.setAttribute("role", "img");
    svg.setAttribute("aria-label", T("Atendimentos, faltas e cancelamentos por dia nos últimos 30 dias"));

    const add = (tag, attrs, texto) => {
        const e = document.createElementNS(ns, tag);
        for (const [k, v] of Object.entries(attrs)) e.setAttribute(k, v);
        if (texto !== undefined) e.textContent = texto;
        svg.appendChild(e);
        return e;
    };

    // Linhas de grade
    const marcas = max <= 4 ? max : 4;
    for (let i = 0; i <= marcas; i++) {
        const v = Math.round(max * i / marcas);
        add("line", { x1: mE, x2: L, y1: y(v), y2: y(v), class: "eixo" });
        add("text", { x: mE - 6, y: y(v) + 3, "text-anchor": "end" }, v);
    }

    const cores = { atendidos: "#0f766e", faltas: "#dc2626", cancelados: "#94a3b8" };
    dias.forEach((d, i) => {
        const x = mE + i * passo + (passo - larg) / 2;
        let base = 0;
        for (const serie of ["atendidos", "faltas", "cancelados"]) {
            const v = d[serie];
            if (!v) continue;
            const r = add("rect", { x, width: larg, y: y(base + v), height: y(base) - y(base + v), fill: cores[serie], rx: 2 });
            const t = document.createElementNS(ns, "title");
            t.textContent = `${d.data.toLocaleDateString(window.idioma)}: ${v} ${T(serie)}`;
            r.appendChild(t);
            base += v;
        }
        if (i % 5 === 4 || i === dias.length - 1)
            add("text", { x: x + larg / 2, y: A - 6, "text-anchor": "middle" }, d.data.toLocaleDateString(window.idioma, { day: "2-digit", month: "2-digit" }));
    });

    area.appendChild(svg);
})();
