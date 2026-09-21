import { useEffect, useState } from "react";
import { librariesApi, type ScanStatus } from "../api/endpoints";

/** Une por id conservando el orden de llegada: el escaner manda cada tomo una sola vez. */
function mergeById<T extends { id: string }>(carried: T[] | undefined, incoming: T[] | undefined): T[] {
    const base = carried ?? [];
    const fresh = incoming ?? [];
    if (fresh.length === 0) return base;
    const seen = new Set(base.map((item) => item.id));
    const added = fresh.filter((item) => !seen.has(item.id));
    return added.length === 0 ? base : [...base, ...added];
}

export function useScanProgress(libraryId: string, trigger = 0): ScanStatus | null {
    const [status, setStatus] = useState<{ key: string; value: ScanStatus | null } | null>(null);

    const snapshotKey = `${libraryId}#${String(trigger)}`;

    useEffect(() => {
        // Sin biblioteca no hay nada que preguntar: la ficha de serie monta el hook antes de
        // saber a que biblioteca pertenece, y preguntarlo con el id vacio es un 404 seguro.
        if (libraryId.length === 0) {
            queueMicrotask(() => {
                setStatus({ key: libraryId, value: null });
            });
            return;
        }

        let cancelled = false;
        librariesApi.scanStatus(libraryId).then(
            (current) => {
                if (!cancelled) setStatus({ key: libraryId, value: current });
            },
            () => {
                if (!cancelled) setStatus({ key: libraryId, value: null });
            },
        );

        return () => {
            cancelled = true;
        };
    }, [snapshotKey, libraryId]);

    const current = status?.key === libraryId ? status.value : null;
    const live = current !== null && !current.finished;

    useEffect(() => {
        if (!live) return;

        let cancelled = false;
        const source = new EventSource(librariesApi.scanStreamUrl(libraryId), { withCredentials: true });

        source.onmessage = (event) => {
            const parsed = JSON.parse(event.data as string) as ScanStatus;
            if (cancelled) return;

            // Lo creado se acumula aqui, en el manejador del propio flujo. Acumularlo en el
            // render de la pagina lo perdia: el evento siguiente reemplaza este estado y el
            // render a medias que iba a guardar el lote anterior se descarta con el.
            setStatus((previous) => {
                const carried = previous?.key === libraryId ? previous.value : null;
                return {
                    key: libraryId,
                    value: {
                        ...parsed,
                        createdMedia: mergeById(carried?.createdMedia, parsed.createdMedia),
                        createdSeries: mergeById(carried?.createdSeries, parsed.createdSeries),
                    },
                };
            });

            // El servidor corta el flujo al terminar, y EventSource lee ese cierre como caida
            // y reconecta cada 3 s. Cerrarlo aqui es lo que rompe ese ciclo.
            if (parsed.finished) source.close();
        };

        // Un corte de red lo reintenta EventSource solo; cerrarlo aqui rompería esa reconexión.
        source.onerror = () => undefined;

        return () => {
            cancelled = true;
            source.close();
        };
    }, [libraryId, live]);

    return current;
}

export { formatDurationSeconds as formatDuration } from "./duration";

/**
 * Que esta haciendo el escaner, en una linea. El mensaje del servidor manda cuando lo hay:
 * lleva las cuentas reales ("3 sin carpeta, 12 nuevas"), que dicen mas que el nombre de la fase.
 */
export function scanLabel(progress: ScanStatus): string {
    if (progress.queued) return "EN COLA";
    if (progress.phase === "ProcessingMedia") return progress.currentMedia ?? progress.currentSeries ?? "LEYENDO TOMOS";
    if (progress.phase === "ProcessingSeries") return progress.currentSeries ?? "LEYENDO TOMOS";
    if (progress.message) return progress.message;

    switch (progress.phase) {
        case "Started":
            return "PREPARANDO";
        case "WalkingLibrary":
            return "RECORRIENDO CARPETAS";
        case "Completed":
            return "COMPLETADO";
        case "Failed":
            return "ERROR EN EL ESCANEO";
        default:
            return "INDEXANDO";
    }
}
