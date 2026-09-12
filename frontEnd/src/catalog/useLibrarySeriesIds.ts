import { useEffect, useState } from "react";
import { MAX_PAGE_SIZE, seriesApi } from "../api/endpoints";

interface Collected {
	libraryId: string;
	ids: Set<string>;
}

/**
 * Conjunto de ids de serie de una biblioteca.
 *
 * `GET /api/v2/media` no acepta `libraryId`, así que las secciones de medios de la
 * home no pueden filtrarse en el servidor. Se resuelve aquí: se pagina la lista de
 * series de la biblioteca una vez y las secciones filtran por `seriesId` contra este
 * conjunto. Con "todas" seleccionado devuelve `null` y no se pide nada.
 *
 * El resultado se guarda junto a la biblioteca que lo produjo: así una respuesta que
 * llega tarde, después de cambiar de sector, se descarta sola al derivar el valor.
 */
export function useLibrarySeriesIds(libraryId: string | null): Set<string> | null {
	const [collected, setCollected] = useState<Collected | null>(null);

	useEffect(() => {
		if (!libraryId) return;

		let cancelled = false;
		const collect = async () => {
			const ids = new Set<string>();
			let page = 0;
			let totalPages = 1;
			while (page < totalPages && !cancelled) {
				const result = await seriesApi.list(libraryId, page, MAX_PAGE_SIZE);
				for (const series of result.data) {
					ids.add(series.id);
				}
				totalPages = result.totalPages;
				page += 1;
			}
			if (!cancelled) {
				setCollected({ libraryId, ids });
			}
		};

		void collect().catch(() => {
			// Si falla, no se filtra: es preferible enseñar de más que dejar la home vacía.
		});

		return () => {
			cancelled = true;
		};
	}, [libraryId]);

	if (!libraryId || collected?.libraryId !== libraryId) return null;
	return collected.ids;
}
