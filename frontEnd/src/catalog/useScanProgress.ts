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
/**
 * Progreso del escaneo.
 *
 * Primero se pregunta el estado, que es una consulta barata, y **solo si hay un escaneo sin
 * terminar** se abre el flujo SSE. Tener una conexion abierta en cada ficha de biblioteca
 * sin que nadie escanee no aporta nada, y es la que se quedaba reconectando en bucle.
 *
 * `trigger` sirve para volver a preguntar tras lanzar un escaneo: la cola marca la
 * biblioteca al encolarla, asi que el estado ya viene sin terminar y el flujo se abre.
 */
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

			setStatus({ key: libraryId, value: parsed });

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
