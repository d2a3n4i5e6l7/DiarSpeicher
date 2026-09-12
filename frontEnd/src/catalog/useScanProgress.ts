import { useEffect, useState } from "react";
import { librariesApi, type ScanStatus } from "../api/endpoints";

/**
 * Progreso del escaneo por SSE.
 *
 * SSE y no websocket porque el progreso solo baja: no hay nada que mandar al servidor y un
 * canal HTTP corriente atraviesa el gateway sin configurarle nada. `EventSource` ademas
 * reconecta solo, asi que no hay que escribir reintentos.
 *
 * Se pide primero el estado actual, porque quien entra a mitad del escaneo se quedaria en
 * blanco hasta el siguiente evento.
 */
export function useScanProgress(libraryId: string, enabled: boolean): ScanStatus | null {
	const [status, setStatus] = useState<{ key: string; value: ScanStatus | null } | null>(null);

	useEffect(() => {
		if (!enabled || !libraryId) return;

		let cancelled = false;

		librariesApi.scanStatus(libraryId).then(
			(current) => {
				if (!cancelled && current) setStatus({ key: libraryId, value: current });
			},
			() => {
				/* sin estado previo: se espera al primer evento */
			},
		);

		const source = new EventSource(librariesApi.scanStreamUrl(libraryId), { withCredentials: true });

		source.onmessage = (event) => {
			const parsed = JSON.parse(event.data as string) as ScanStatus;
			if (!cancelled) setStatus({ key: libraryId, value: parsed });
		};

		// Un error de red lo reintenta EventSource solo; cerrarlo aqui rompería esa reconexión.
		source.onerror = () => undefined;

		return () => {
			cancelled = true;
			source.close();
		};
	}, [libraryId, enabled]);

	return status?.key === libraryId ? status.value : null;
}

/** "2 min 10 s". Sin decimales: una estimacion con segundos exactos finge una precision que no tiene. */
export function formatDuration(seconds: number): string {
	if (seconds < 60) return `${String(Math.max(seconds, 1))} s`;

	const minutes = Math.floor(seconds / 60);
	const rest = seconds % 60;
	if (minutes < 60) return rest > 0 ? `${String(minutes)} min ${String(rest)} s` : `${String(minutes)} min`;

	const hours = Math.floor(minutes / 60);

	return `${String(hours)} h ${String(minutes % 60)} min`;
}

/**
 * Que esta haciendo el escaner, en una linea. El mensaje del servidor manda cuando lo hay:
 * lleva las cuentas reales ("3 sin carpeta, 12 nuevas"), que dicen mas que el nombre de la fase.
 */
export function scanLabel(progress: ScanStatus): string {
	if (progress.queued) return "EN COLA";
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
