import { Alert, Box, CircularProgress, Pagination, Typography } from "@mui/material";
import CollectionsBookmarkIcon from "@mui/icons-material/CollectionsBookmark";
import { useEffect, useState } from "react";
import PageHeader from "../components/PageHeader";
import MediaCard from "../components/MediaCard";
import { useLibraryFilter } from "../catalog/LibraryFilterContext";
import { seriesApi, type PageResponse, type SeriesItem } from "../api/endpoints";
import { COVER_GRID } from "../catalog/layout";
import { DS } from "../theme";

const PAGE_SIZE = 24;

interface Result {
	key: string;
	data?: PageResponse<SeriesItem>;
	error?: string;
}

export default function SeriesGridPage() {
	const { libraryId } = useLibraryFilter();
	// La página vive junto a su biblioteca: al cambiar de sector vuelve sola a la
	// primera, sin un efecto que la reinicie.
	const [pageState, setPageState] = useState<{ libraryId: string | null; page: number }>({ libraryId, page: 0 });
	const page = pageState.libraryId === libraryId ? pageState.page : 0;

	const [result, setResult] = useState<Result | null>(null);
	const requestKey = `${libraryId ?? "*"}#${String(page)}`;

	useEffect(() => {
		let mounted = true;
		seriesApi
			.list(libraryId, page, PAGE_SIZE)
			.then((data) => {
				if (mounted) setResult({ key: requestKey, data });
			})
			.catch((err: unknown) => {
				if (mounted) {
					setResult({
						key: requestKey,
						error: err instanceof Error ? err.message : "No se pudo cargar la lista de series.",
					});
				}
			});
		return () => {
			mounted = false;
		};
	}, [libraryId, page, requestKey]);

	// Solo cuenta la respuesta de la petición actual: una que llegue tarde se ignora.
	const fresh = result?.key === requestKey ? result : null;
	const loading = fresh === null;
	const series = fresh?.data?.data ?? [];
	const totalPages = Math.max(1, fresh?.data?.totalPages ?? 1);
	const total = fresh?.data?.total ?? 0;

	let content: React.ReactNode;
	if (loading) {
		content = (
			<Box sx={{ display: "flex", justifyContent: "center", py: 10 }}>
				<CircularProgress size={30} thickness={5} />
			</Box>
		);
	} else if (series.length === 0) {
		content = (
			<Box sx={{ textAlign: "center", py: 10 }}>
				<CollectionsBookmarkIcon sx={{ fontSize: 44, color: DS.borderRed, mb: 1.5 }} />
				<Typography
					sx={{
						fontFamily: "'Rajdhani', sans-serif",
						fontSize: "16px",
						fontWeight: 700,
						letterSpacing: "1.5px",
						textTransform: "uppercase",
						color: DS.platinum,
					}}
				>
					Sin series indexadas
				</Typography>
				<Typography sx={{ fontFamily: "'Inter', sans-serif", fontSize: "13px", color: DS.muted, mt: 0.5 }}>
					Escanea una biblioteca para que el motor pueble este sector.
				</Typography>
			</Box>
		);
	} else {
		content = (
			<Box
				sx={{
					display: "grid",
					gap: 2.5,
					gridTemplateColumns: COVER_GRID,
				}}
			>
				{series.map((item) => (
					<MediaCard
						key={item.id}
						to={`/series/${item.id}`}
						title={item.name}
						subtitle={`${String(item.mediaCount)} TOMOS`}
						coverUrl={seriesApi.thumbnailUrl(item.id)}
						width="100%"
					/>
				))}
			</Box>
		);
	}

	return (
		<Box>
			<PageHeader title="Series" subtitle={`${String(total)} series indexadas en el sector seleccionado.`} />

			{fresh?.error && (
				<Alert severity="error" sx={{ mb: 3 }}>
					{fresh.error}
				</Alert>
			)}

			{content}

			{totalPages > 1 && (
				<Box sx={{ display: "flex", justifyContent: "center", mt: 4 }}>
					<Pagination
						count={totalPages}
						page={page + 1}
						onChange={(_, next) => setPageState({ libraryId, page: next - 1 })}
						shape="rounded"
						sx={{
							"& .MuiPaginationItem-root": {
								fontFamily: "'JetBrains Mono', monospace",
								fontSize: "12px",
								borderRadius: 0,
								border: `1px solid ${DS.border}`,
								color: DS.muted,
							},
							"& .Mui-selected": {
								backgroundColor: `${DS.red} !important`,
								borderColor: DS.redGlow,
								color: "#FFFFFF",
							},
						}}
					/>
				</Box>
			)}
		</Box>
	);
}
