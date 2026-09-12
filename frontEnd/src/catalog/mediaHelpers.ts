import type { MediaItem } from "../api/endpoints";

/** Porcentaje leído de un tomo, 0-100. Un tomo marcado como completo vale 100. */
export function readProgress(media: MediaItem): number {
	if (media.isCompleted) return 100;
	if (!media.pages || media.currentPage === undefined || media.currentPage <= 0) return 0;
	return Math.min(100, Math.round((media.currentPage / media.pages) * 100));
}

export function formatBytes(bytes: number): string {
	if (!bytes) return "0 B";
	const units = ["B", "KB", "MB", "GB"];
	const i = Math.min(units.length - 1, Math.floor(Math.log(bytes) / Math.log(1024)));
	return `${(bytes / Math.pow(1024, i)).toFixed(i === 0 ? 0 : 1)} ${units[i]}`;
}

/** Minutos estimados de lectura. 1,5 páginas por minuto es el ritmo medio de un tomo. */
export function estimatedMinutes(pages: number): number {
	return Math.max(1, Math.round(pages / 1.5));
}

export function formatDuration(minutes: number): string {
	if (minutes < 60) return `${String(minutes)} MIN`;
	const hours = Math.floor(minutes / 60);
	const rest = minutes % 60;
	return rest === 0 ? `${String(hours)} H` : `${String(hours)} H ${String(rest)} MIN`;
}

/** El título del medio: el de los metadatos si el escáner lo extrajo, si no el nombre. */
export function mediaTitle(media: MediaItem): string {
	return media.metadata?.title?.trim() || media.name;
}

export function mediaSubtitle(media: MediaItem): string {
	const ext = media.extension.replace(/^\./, "").toUpperCase();
	return `${ext} · ${String(media.pages)} PÁGS`;
}
