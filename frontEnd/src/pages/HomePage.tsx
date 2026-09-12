import { Alert, Box, CircularProgress, Typography } from "@mui/material";
import { useCallback, useEffect, useState } from "react";
import PageHeader from "../components/PageHeader";
import SectionRow from "../components/SectionRow";
import MediaCard from "../components/MediaCard";
import { useLibraryFilter } from "../catalog/LibraryFilterContext";
import { useLibrarySeriesIds } from "../catalog/useLibrarySeriesIds";
import { mediaTitle, mediaSubtitle, readProgress } from "../catalog/mediaHelpers";
import { mediaApi, seriesApi, type MediaItem, type SeriesItem } from "../api/endpoints";
import { DS } from "../theme";

const ROW_SIZE = 20;

interface Result {
	key: string;
	reading?: MediaItem[];
	recent?: MediaItem[];
	series?: SeriesItem[];
	error?: string;
}

export default function HomePage() {
	const { libraryId } = useLibraryFilter();
	const librarySeriesIds = useLibrarySeriesIds(libraryId);

	// El resultado se guarda junto al sector que lo pidió: así se deriva si sigue
	// vigente y una respuesta que llega tarde no pisa a la del sector actual.
	const [result, setResult] = useState<Result | null>(null);
	const requestKey = libraryId ?? "*";

	useEffect(() => {
		let mounted = true;
		Promise.all([mediaApi.keepReading(), mediaApi.latest(ROW_SIZE), seriesApi.list(libraryId, 0, ROW_SIZE)])
			.then(([reading, recent, seriesPage]) => {
				if (mounted) setResult({ key: requestKey, reading, recent, series: seriesPage.data });
			})
			.catch((err: unknown) => {
				if (mounted) {
					setResult({
						key: requestKey,
						error: err instanceof Error ? err.message : "No se pudo cargar el catálogo.",
					});
				}
			});

		return () => {
			mounted = false;
		};
	}, [libraryId, requestKey]);

	const fresh = result?.key === requestKey ? result : null;
	const loading = fresh === null;
	const keepReading = fresh?.reading ?? [];
	const latest = fresh?.recent ?? [];
	const series = fresh?.series ?? [];

	// El filtro por biblioteca solo llega al servidor en `/series`. Para los medios se
	// aplica aquí contra las series de la biblioteca elegida.
	const inLibrary = useCallback(
		(media: MediaItem) => {
			if (!librarySeriesIds) return true;
			return media.seriesId !== undefined && librarySeriesIds.has(media.seriesId);
		},
		[librarySeriesIds]
	);

	const visibleReading = keepReading.filter(inLibrary);
	const visibleLatest = latest.filter(inLibrary);

	if (loading) {
		return (
			<Box sx={{ display: "flex", flexDirection: "column", alignItems: "center", gap: 2, py: 10 }}>
				<CircularProgress size={30} thickness={5} />
				<Typography sx={{ fontFamily: "'JetBrains Mono', monospace", fontSize: "11px", letterSpacing: "1px", color: DS.subtle }}>
					MONTANDO ÍNDICE DEL ARCHIVO...
				</Typography>
			</Box>
		);
	}

	return (
		<Box>
			<PageHeader
				title="Archivo DiarSpeicher"
				subtitle="Continúa donde lo dejaste o entra por la última ingesta del nodo."
			/>

			{fresh?.error && (
				<Alert severity="error" sx={{ mb: 3 }}>
					{fresh.error}
				</Alert>
			)}

			<SectionRow
				title="Continuar leyendo"
				count={visibleReading.length}
				empty={visibleReading.length === 0 ? "No hay ninguna lectura abierta en este sector." : undefined}
			>
				{visibleReading.map((media) => (
					<MediaCard
						key={media.id}
						to={`/media/${media.id}`}
						title={mediaTitle(media)}
						subtitle={mediaSubtitle(media)}
						coverUrl={mediaApi.thumbnailUrl(media.id)}
						progress={readProgress(media)}
						badge={media.currentPage ? `P${String(media.currentPage)}` : undefined}
					/>
				))}
			</SectionRow>

			<SectionRow
				title="Añadido reciente"
				count={visibleLatest.length}
				empty={visibleLatest.length === 0 ? "Sin ingestas recientes en este sector." : undefined}
			>
				{visibleLatest.map((media) => (
					<MediaCard
						key={media.id}
						to={`/media/${media.id}`}
						title={mediaTitle(media)}
						subtitle={mediaSubtitle(media)}
						coverUrl={mediaApi.thumbnailUrl(media.id)}
						progress={readProgress(media)}
					/>
				))}
			</SectionRow>

			<SectionRow
				title="Series"
				count={series.length}
				moreTo="/series"
				empty={series.length === 0 ? "Este sector no tiene series indexadas." : undefined}
			>
				{series.map((item) => (
					<MediaCard
						key={item.id}
						to={`/series/${item.id}`}
						title={item.name}
						subtitle={`${String(item.mediaCount)} TOMOS`}
						coverUrl={seriesApi.thumbnailUrl(item.id)}
					/>
				))}
			</SectionRow>
		</Box>
	);
}
