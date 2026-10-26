(function () {
    "use strict";

    const dropZones = new Map();

    const theme = {
        actual() {
            return document.documentElement.getAttribute("data-theme") || "dark";
        },
        aplicar(valor) {
            const t = valor === "light" ? "light" : "dark";
            document.documentElement.setAttribute("data-theme", t);
            try { localStorage.setItem("plagio.theme", t); } catch (e) { /* modo privado */ }
            const meta = document.querySelector('meta[name="theme-color"]');
            if (meta) meta.setAttribute("content", t === "dark" ? "#081018" : "#f4f7fa");
            return t;
        },
        alternar() {
            return this.aplicar(this.actual() === "dark" ? "light" : "dark");
        }
    };

    const blind = {
        obtener() {
            try { return localStorage.getItem("plagio.blind") === "1"; } catch (e) { return false; }
        },
        guardar(valor) {
            try { localStorage.setItem("plagio.blind", valor ? "1" : "0"); } catch (e) { /* ignorar */ }
        }
    };

    const sidebar = {
        alternar() {
            document.body.classList.toggle("sidebar-open");
            return document.body.classList.contains("sidebar-open");
        },
        cerrar() {
            document.body.classList.remove("sidebar-open");
        }
    };

    const drop = {
        bind(zoneId, inputId) {
            const zone = document.getElementById(zoneId);
            const input = document.getElementById(inputId);
            if (!zone || !input) return;

            if (dropZones.has(zoneId)) {
                drop.unbind(zoneId);
            }

            let dragDepth = 0;

            const onDragEnter = (e) => {
                e.preventDefault();
                dragDepth++;
                zone.classList.add("is-dragover");
            };

            const onDragOver = (e) => {
                e.preventDefault();
                if (e.dataTransfer) {
                    e.dataTransfer.dropEffect = "copy";
                }
            };

            const onDragLeave = (e) => {
                e.preventDefault();
                dragDepth = Math.max(0, dragDepth - 1);
                if (dragDepth === 0) zone.classList.remove("is-dragover");
            };

            const onDrop = (e) => {
                e.preventDefault();
                dragDepth = 0;
                zone.classList.remove("is-dragover");

                const archivos = e.dataTransfer && e.dataTransfer.files;
                if (!archivos || archivos.length === 0) return;

                try {
                    const transferencia = new DataTransfer();
                    transferencia.items.add(archivos[0]);
                    input.files = transferencia.files;
                    input.dispatchEvent(new Event("change", { bubbles: true }));
                } catch (err) {
                    console.error("No se pudo asignar el archivo al input.", err);
                }
            };

            const onClick = (e) => {
                if (e.target.closest("button, a, label, input, .dropzone-clear")) return;
                input.click();
            };

            zone.addEventListener("dragenter", onDragEnter);
            zone.addEventListener("dragover", onDragOver);
            zone.addEventListener("dragleave", onDragLeave);
            zone.addEventListener("drop", onDrop);
            zone.addEventListener("click", onClick);

            dropZones.set(zoneId, { zone, onDragEnter, onDragOver, onDragLeave, onDrop, onClick });
        },

        unbind(zoneId) {
            const item = dropZones.get(zoneId);
            if (!item) return;
            item.zone.removeEventListener("dragenter", item.onDragEnter);
            item.zone.removeEventListener("dragover", item.onDragOver);
            item.zone.removeEventListener("dragleave", item.onDragLeave);
            item.zone.removeEventListener("drop", item.onDrop);
            item.zone.removeEventListener("click", item.onClick);
            item.zone.classList.remove("is-dragover");
            dropZones.delete(zoneId);
        },

        clear(inputId) {
            const input = document.getElementById(inputId);
            if (input) {
                try { input.value = ""; } catch (e) { /* algunos navegadores lo bloquean */ }
            }
        }
    };

    window.plagio = {
        theme,
        blind,
        sidebar,
        drop,
        confirmar(mensaje) {
            return window.confirm(mensaje || "¿Desea continuar?");
        },
        descargarTexto(nombre, contenido, tipo) {
            const blob = new Blob([contenido], { type: tipo || "text/plain;charset=utf-8" });
            const url = URL.createObjectURL(blob);
            const a = document.createElement("a");
            a.href = url;
            a.download = nombre || "archivo.txt";
            document.body.appendChild(a);
            a.click();
            a.remove();
            setTimeout(() => URL.revokeObjectURL(url), 1000);
        },
        enfocar(id) {
            const elemento = document.getElementById(id);
            if (elemento) {
                elemento.focus();
                elemento.scrollIntoView({ behavior: "smooth", block: "center" });
            }
        }
    };
})();
