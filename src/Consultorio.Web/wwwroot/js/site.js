/* =====================================================================
   Consultório Médico - JavaScript comum a todas as páginas (sem bibliotecas)
   ===================================================================== */
/**
 * Texto no idioma da página: window.textos vem do servidor (partial _TextosJs, a partir dos Resources/Textos*.resx).
 * A chave é o texto em português; {0}, {1}... são trocados pelos argumentos.
 */
window.traduzir = function (texto, ...args) {
    let s = (window.textos && window.textos[texto]) || texto;
    args.forEach((a, i) => { s = s.split("{" + i + "}").join(a); });
    return s;
};
/** Idioma da página (atributo lang do <html>), para datas e números no JavaScript. */
window.idioma = document.documentElement.lang || "pt-BR";

(function () {
    "use strict";
    const T = window.traduzir;

    const csrf = () => document.querySelector('meta[name="csrf-token"]')?.content ?? "";
    // Caminho base da aplicação (ex.: "/consultorio/" quando publicada em um diretório virtual do IIS)
    const base = document.querySelector('meta[name="app-base"]')?.content || "/";
    const rota = url => url.startsWith("/") ? base + url.slice(1) : url;

    /** Chamada ao servidor com JSON. Lança Error com a mensagem do servidor em caso de falha. */
    async function api(url, opcoes = {}) {
        let resp;
        try {
            resp = await fetch(rota(url), {
                method: opcoes.method || "GET",
                headers: {
                    "Accept": "application/json",
                    "Content-Type": "application/json",
                    "X-Requested-With": "fetch",
                    "RequestVerificationToken": csrf()
                },
                body: opcoes.body !== undefined ? JSON.stringify(opcoes.body) : undefined,
                credentials: "same-origin"
            });
        } catch {
            throw new Error(T("Não foi possível falar com o servidor. Verifique a conexão e tente novamente."));
        }
        if (resp.status === 401) {
            location.href = rota("/Conta/Login") + "?returnUrl=" + encodeURIComponent(location.pathname + location.search);
            throw new Error(T("Sua sessão expirou. Entre novamente."));
        }
        if (resp.status === 403) throw new Error(T("Seu perfil não tem permissão para esta operação."));
        const texto = await resp.text();
        let dados = null;
        try { dados = texto ? JSON.parse(texto) : null; } catch { /* resposta não JSON */ }
        if (!resp.ok) throw new Error(dados?.erro || T("Não foi possível concluir a operação. Tente novamente."));
        return dados;
    }

    /** Mensagem flutuante no canto da tela. */
    function toast(mensagem, tipo) {
        const area = document.getElementById("toasts") || document.body;
        const el = document.createElement("div");
        el.className = "toast" + (tipo === "erro" ? " erro" : "");
        el.textContent = mensagem;
        area.appendChild(el);
        setTimeout(() => el.remove(), tipo === "erro" ? 7000 : 4000);
    }

    function somenteDigitos(v) { return (v || "").replace(/\D/g, ""); }

    const mascaras = {
        cpf: d => d.slice(0, 11).replace(/(\d{3})(\d)/, "$1.$2").replace(/(\d{3})(\d)/, "$1.$2").replace(/(\d{3})(\d{1,2})$/, "$1-$2"),
        cnpj: d => d.slice(0, 14).replace(/(\d{2})(\d)/, "$1.$2").replace(/(\d{3})(\d)/, "$1.$2").replace(/(\d{3})(\d)/, "$1/$2").replace(/(\d{4})(\d{1,2})$/, "$1-$2"),
        cep: d => d.slice(0, 8).replace(/(\d{5})(\d)/, "$1-$2"),
        telefone: d => {
            d = d.slice(0, 11);
            if (d.length <= 2) return d.length ? "(" + d : "";
            if (d.length <= 6) return `(${d.slice(0, 2)}) ${d.slice(2)}`;
            if (d.length <= 10) return `(${d.slice(0, 2)}) ${d.slice(2, 6)}-${d.slice(6)}`;
            return `(${d.slice(0, 2)}) ${d.slice(2, 7)}-${d.slice(7)}`;
        }
    };

    window.App = { api, toast, somenteDigitos };

    // ---------- Comportamentos declarativos (atributos data-*) ----------
    document.addEventListener("DOMContentLoaded", () => {
        // Envia o formulário ao mudar um filtro
        document.querySelectorAll("[data-enviar-ao-mudar]").forEach(el =>
            el.addEventListener("change", () => el.form?.submit()));

        // Menu lateral no celular
        const sidebar = document.getElementById("sidebar");
        document.querySelectorAll("[data-alternar-menu]").forEach(b =>
            b.addEventListener("click", e => { e.stopPropagation(); sidebar?.classList.toggle("aberta"); }));
        document.addEventListener("click", e => {
            if (sidebar?.classList.contains("aberta") && !sidebar.contains(e.target)) sidebar.classList.remove("aberta");
        });

        // Mensagens de sucesso somem sozinhas
        document.querySelectorAll("[data-fechar-auto]").forEach(el =>
            setTimeout(() => { el.style.transition = "opacity .4s"; el.style.opacity = "0"; setTimeout(() => el.remove(), 400); }, 5000));

        // Fecha menus "mais ações" ao clicar fora
        document.addEventListener("click", e => {
            document.querySelectorAll("details.menu-mais[open]").forEach(d => { if (!d.contains(e.target)) d.open = false; });
        });

        // Confirmação antes de enviar
        document.addEventListener("submit", e => {
            const form = e.target;
            const botao = e.submitter;
            if (e.defaultPrevented || form.method === "dialog") return;
            const msg = botao?.dataset.confirmar || form.dataset.confirmar;
            if (msg && !confirm(msg)) { e.preventDefault(); return; }

            // Remove máscara dos campos que o servidor espera só com dígitos
            (form.dataset.somenteDigitos || "").split(",").filter(Boolean).forEach(nome => {
                const campo = form.elements[nome.trim()];
                if (campo && campo.value !== undefined) campo.value = somenteDigitos(campo.value);
            });
            form.dataset.enviando = "1";
            if (botao && !botao.name) setTimeout(() => { botao.disabled = true; }, 0);
        });

        // Máscaras de digitação
        document.querySelectorAll("[data-mascara]").forEach(el => {
            const fn = mascaras[el.dataset.mascara];
            if (!fn) return;
            const aplicar = () => { el.value = fn(somenteDigitos(el.value)); };
            el.addEventListener("input", aplicar);
            aplicar();
        });

        // Busca de endereço pelo CEP (ViaCEP)
        document.querySelectorAll("[data-buscar-cep]").forEach(el => el.addEventListener("blur", async () => {
            const cep = somenteDigitos(el.value);
            if (cep.length !== 8) return;
            try {
                const r = await fetch(`https://viacep.com.br/ws/${cep}/json/`);
                const d = await r.json();
                if (d.erro) return;
                const f = el.form;
                const preencher = (nome, valor) => { const c = f.elements[nome]; if (c && !c.value) c.value = valor || ""; };
                preencher("Logradouro", d.logradouro); preencher("Bairro", d.bairro);
                preencher("Cidade", d.localidade); preencher("Uf", d.uf);
            } catch { /* sem internet: o usuário digita o endereço */ }
        }));

        // Listas dinâmicas: <fieldset data-lista="Prefixo"> com <template> e botões adicionar/remover
        document.querySelectorAll("[data-lista]").forEach(lista => {
            const prefixo = lista.dataset.lista;
            const container = lista.querySelector(".lista-itens");
            const template = lista.querySelector("template");
            const reindexar = () => {
                const regex = new RegExp("^" + prefixo + "\\[\\d+\\]");
                container.querySelectorAll(":scope > .item-lista").forEach((item, i) =>
                    item.querySelectorAll("[name]").forEach(c => { c.name = c.name.replace(regex, `${prefixo}[${i}]`); }));
            };
            lista.querySelector("[data-adicionar-item]")?.addEventListener("click", () => {
                const i = container.querySelectorAll(":scope > .item-lista").length;
                const html = template.innerHTML.replaceAll("__i__", i);
                container.insertAdjacentHTML("beforeend", html);
                container.lastElementChild.querySelector("input:not([type=hidden]), select")?.focus();
            });
            container.addEventListener("click", e => {
                const b = e.target.closest("[data-remover-item]");
                if (!b) return;
                b.closest(".item-lista").remove();
                reindexar();
            });
        });

        // Abas (com âncora na URL: #evolucoes, #resumo...)
        document.querySelectorAll("[data-abas]").forEach(abas => {
            const botoes = abas.querySelectorAll("[data-aba]");
            const ativar = id => {
                botoes.forEach(b => b.classList.toggle("ativa", b.dataset.aba === id));
                abas.querySelectorAll(".aba").forEach(a => a.classList.toggle("ativa", a.id === id));
            };
            botoes.forEach(b => b.addEventListener("click", () => {
                ativar(b.dataset.aba);
                history.replaceState(null, "", "#" + b.dataset.aba);
            }));
            const existe = id => [...botoes].some(b => b.dataset.aba === id);
            const inicial = location.hash.slice(1);
            ativar(existe(inicial) ? inicial : botoes[0]?.dataset.aba);
            // Links "#aba" na própria página (ex.: botão "+ Anexo")
            window.addEventListener("hashchange", () => { const id = location.hash.slice(1); if (existe(id)) ativar(id); });
        });

        // Botão "Editar" de uma tabela preenche o formulário ao lado
        document.querySelectorAll("[data-editar-em]").forEach(b => b.addEventListener("click", () => {
            const form = document.querySelector(b.dataset.editarEm);
            const valores = JSON.parse(b.dataset.valores);
            for (const [nome, valor] of Object.entries(valores)) {
                form.querySelectorAll(`[name="${nome}"]`).forEach(c => {
                    if (c.type === "checkbox") c.checked = Array.isArray(valor) ? valor.includes(c.value) : !!valor;
                    else if (c.type === "hidden" && form.querySelector(`input[type=checkbox][name="${nome}"]`)) { /* par do checkbox */ }
                    else c.value = valor ?? "";
                });
            }
            const titulo = form.querySelector("[data-titulo-form]");
            if (titulo) { titulo.dataset.original ??= titulo.textContent; titulo.textContent = "Editar"; }
            form.scrollIntoView({ behavior: "smooth", block: "start" });
            form.querySelector("input:not([type=hidden])")?.focus();
        }));
        document.querySelectorAll("[data-limpar-form]").forEach(b => b.addEventListener("click", () => {
            const form = b.form;
            setTimeout(() => {
                form.querySelectorAll('input[type=hidden][name="Id"]').forEach(h => h.value = "");
                const titulo = form.querySelector("[data-titulo-form]");
                if (titulo?.dataset.original) titulo.textContent = titulo.dataset.original;
            }, 0);
        }));

        // Aviso ao sair de um formulário com alterações não salvas
        document.querySelectorAll("form[data-avisar-saida]").forEach(form => {
            let alterado = false;
            form.addEventListener("input", () => { alterado = true; });
            window.addEventListener("beforeunload", e => {
                if (alterado && !form.dataset.enviando) { e.preventDefault(); e.returnValue = ""; }
            });
        });

        // Cálculo do IMC na evolução
        const peso = document.querySelector('[data-imc="peso"]');
        const altura = document.querySelector('[data-imc="altura"]');
        const saida = document.querySelector('[data-imc="resultado"]');
        if (peso && altura && saida) {
            const calcular = () => {
                const p = parseFloat(peso.value), a = parseFloat(altura.value) / 100;
                saida.textContent = p > 0 && a > 0 ? (p / (a * a)).toLocaleString(window.idioma, { minimumFractionDigits: 1, maximumFractionDigits: 1 }) : "—";
            };
            peso.addEventListener("input", calcular);
            altura.addEventListener("input", calcular);
            calcular();
        }

        // Diálogos: botão "Voltar"
        document.querySelectorAll("[data-fechar-dialogo]").forEach(b =>
            b.addEventListener("click", () => b.closest("dialog")?.close()));
    });
})();
