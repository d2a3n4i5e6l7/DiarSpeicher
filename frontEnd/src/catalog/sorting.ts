import type { SortDirection, SortOption } from "../components/CatalogToolbar";
import type { LibraryItem, MediaItem, SeriesItem } from "../api/endpoints";
import { mediaTitle } from "./mediaHelpers";

/**
 * Criterios tomados de lo que ofrecen Komga, Kavita y Audiobookshelf: nombre y
 * "añadido recientemente" cubren casi todo el uso real; el resto son para decidir
 * qué escanear o qué está creciendo.
 */
export const LIBRARY_SORTS: readonly SortOption[] = [
	{ id: "name", label: "Nombre", defaultDirection: "asc" },
	{ id: "createdAt", label: "Fecha de alta", defaultDirection: "desc" },
	{ id: "lastScannedAt", label: "Último escaneo", defaultDirection: "desc" },
	{ id: "mediaCount", label: "Nº de tomos", defaultDirection: "desc" },
	{ id: "seriesCount", label: "Nº de series", defaultDirection: "desc" },
];

export const SERIES_SORTS: readonly SortOption[] = [
	{ id: "name", label: "Nombre", defaultDirection: "asc" },
	{ id: "mediaCount", label: "Nº de tomos", defaultDirection: "desc" },
	{ id: "status", label: "Estado", defaultDirection: "asc" },
];

export const MEDIA_SORTS: readonly SortOption[] = [
	{ id: "name", label: "Nombre", defaultDirection: "asc" },
	{ id: "createdAt", label: "Fecha de subida", defaultDirection: "desc" },
	{ id: "pages", label: "Nº de páginas", defaultDirection: "desc" },
	{ id: "size", label: "Peso", defaultDirection: "desc" },
];

const collator = new Intl.Collator("es", { numeric: true, sensitivity: "base" });

function compareText(a: string | undefined, b: string | undefined): number {
	return collator.compare(a ?? "", b ?? "");
}

function applyDirection(result: number, direction: SortDirection): number {
	return direction === "asc" ? result : -result;
}

/** Una fecha ausente va siempre al final, mande el sentido que mande. */
function compareDates(a: string | undefined, b: string | undefined, direction: SortDirection): number {
	if (!a && !b) return 0;
	if (!a) return 1;
	if (!b) return -1;
	return applyDirection(Date.parse(a) - Date.parse(b), direction);
}

export function filterAndSortLibraries(
	items: readonly LibraryItem[],
	query: string,
	sort: string,
	direction: SortDirection
): LibraryItem[] {
	const needle = query.trim().toLowerCase();
	const filtered = needle
		? items.filter(
				(item) =>
					item.name.toLowerCase().includes(needle) ||
					item.path.toLowerCase().includes(needle) ||
					(item.description ?? "").toLowerCase().includes(needle)
			)
		: [...items];

	return filtered.sort((a, b) => {
		switch (sort) {
			case "createdAt":
				return compareDates(a.createdAt, b.createdAt, direction);
			case "lastScannedAt":
				return compareDates(a.lastScannedAt, b.lastScannedAt, direction);
			case "mediaCount":
				return applyDirection((a.mediaCount ?? 0) - (b.mediaCount ?? 0), direction);
			case "seriesCount":
				return applyDirection((a.seriesCount ?? 0) - (b.seriesCount ?? 0), direction);
			default:
				return applyDirection(compareText(a.name, b.name), direction);
		}
	});
}

export function filterAndSortSeries(
	items: readonly SeriesItem[],
	query: string,
	sort: string,
	direction: SortDirection
): SeriesItem[] {
	const needle = query.trim().toLowerCase();
	const filtered = needle ? items.filter((item) => item.name.toLowerCase().includes(needle)) : [...items];

	return filtered.sort((a, b) => {
		switch (sort) {
			case "mediaCount":
				return applyDirection(a.mediaCount - b.mediaCount, direction);
			case "status":
				return applyDirection(compareText(a.status, b.status), direction);
			default:
				return applyDirection(compareText(a.name, b.name), direction);
		}
	});
}

export function filterAndSortMedia(
	items: readonly MediaItem[],
	query: string,
	sort: string,
	direction: SortDirection
): MediaItem[] {
	const needle = query.trim().toLowerCase();
	const filtered = needle ? items.filter((item) => mediaTitle(item).toLowerCase().includes(needle)) : [...items];

	return filtered.sort((a, b) => {
		switch (sort) {
			case "createdAt":
				return compareDates(a.createdAt, b.createdAt, direction);
			case "pages":
				return applyDirection(a.pages - b.pages, direction);
			case "size":
				return applyDirection(a.size - b.size, direction);
			default:
				return applyDirection(compareText(mediaTitle(a), mediaTitle(b)), direction);
		}
	});
}
