import type { MediaItem } from "../api/endpoints";
import { formatDurationMinutes } from "./duration";

export { formatBytes } from "./bytes";

export function readProgress(media: MediaItem): number {
	if (media.isCompleted) return 100;
	if (!media.pages || media.currentPage === undefined || media.currentPage <= 0) return 0;
	return Math.min(100, Math.round((media.currentPage / media.pages) * 100));
}

export function estimatedMinutes(pages: number): number {
	return Math.max(1, Math.round(pages / 1.5));
}

export function formatDuration(minutes: number): string {
	return formatDurationMinutes(minutes);
}

export function mediaTitle(media: MediaItem): string {
	return media.metadata?.title?.trim() || media.name;
}

export function mediaSubtitle(media: MediaItem): string {
	const ext = media.extension.replace(/^\./, "").toUpperCase();
	return `${ext} · ${String(media.pages)} PÁGS`;
}
