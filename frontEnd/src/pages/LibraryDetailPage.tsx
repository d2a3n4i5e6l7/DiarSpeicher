import { Alert, Box, Button, Chip, CircularProgress, Stack, Tab, Tabs, Typography } from "@mui/material";
import ArrowBackIcon from "@mui/icons-material/ArrowBack";
import SyncIcon from "@mui/icons-material/Sync";
import EditIcon from "@mui/icons-material/Edit";
import LibraryBooksIcon from "@mui/icons-material/LibraryBooks";
import { useEffect, useState } from "react";
import { Link as RouterLink, useParams } from "react-router-dom";
import HudFrame from "../components/HudFrame";
import MediaCard from "../components/MediaCard";
import CatalogToolbar, { type SortDirection } from "../components/CatalogToolbar";
import {
	librariesApi,
	mediaApi,
	seriesApi,
	MAX_PAGE_SIZE,
	type LibraryItem,
	type MediaItem,
	type SeriesItem,
} from "../api/endpoints";
import { filterAndSortMedia, filterAndSortSeries, MEDIA_SORTS, SERIES_SORTS } from "../catalog/sorting";
import { formatBytes, mediaSubtitle, mediaTitle, readProgress } from "../catalog/mediaHelpers";
import { COVER_GRID } from "../catalog/layout";
import { DS } from "../theme";

interface Loaded {
	key: string;
	library?: LibraryItem;
	series?: SeriesItem[];
	volumes?: MediaItem[];
	error?: string;
}

const EMPTY_SERIES: SeriesItem[] = [];
const EMPTY_VOLUMES: MediaItem[] = [];

function Stat({ label, value }: Readonly<{ label: string; value: string }>) {
	return (
		<Box sx={{ px: 2, py: 1, borderLeft: `2px solid ${DS.borderRed}` }}>
			<Typography
				sx={{
					fontFamily: "'Rajdhani', sans-serif",
					fontSize: "11px",
					fontWeight: 700,
					letterSpacing: "1.5px",
					textTransform: "uppercase",
					color: DS.muted,
				}}
			>
				{label}
			</Typography>
			<Typography sx={{ fontFamily: "'JetBrains Mono', monospace", fontSize: "16px", color: "#FFFFFF" }}>
				{value}
			</Typography>
		</Box>
	);
}

export default function LibraryDetailPage() {
	const { id = "" } = useParams();
	const [loaded, setLoaded] = useState<Loaded | null>(null);
	const [tab, setTab] = useState(0);

	const [query, setQuery] = useState("");
	const [seriesSort, setSeriesSort] = useState("name");
	const [seriesDir, setSeriesDir] = useState<SortDirection>("asc");
	const [mediaSort, setMediaSort] = useState("createdAt");
	const [mediaDir, setMediaDir] = useState<SortDirection>("desc");

	useEffect(() => {
		let mounted = true;

		const load = async () => {
			const library = await librariesApi.get(id);

			// Las series de la biblioteca se piden enteras: hacen falta completas para
			// filtrar y ordenar en cliente, que es donde vive el buscador.
			const series: SeriesItem[] = [];
			let page = 0;
			let totalPages = 1;
			while (page < totalPages) {
				const result = await seriesApi.list(id, page, MAX_PAGE_SIZE);
				series.push(...result.data);
				totalPages = result.totalPages;
				page += 1;
			}

			// Los tomos se traen por serie: no hay endpoint que liste medios por biblioteca.
			const perSeries = await Promise.all(
				series.map((item) => seriesApi.media(item.id, 0, MAX_PAGE_SIZE).then((r) => r.data).catch(() => []))
			);

			if (mounted) {
				setLoaded({ key: id, library, series, volumes: perSeries.flat() });
			}
		};

		void load().catch((err: unknown) => {
			if (mounted) {
				setLoaded({ key: id, error: err instanceof Error ? err.message : "No se pudo cargar la biblioteca." });
			}
		});

		return () => {
			mounted = false;
		};
	}, [id]);

	const fresh = loaded?.key === id ? loaded : null;
	const loading = fresh === null;
	const library = fresh?.library ?? null;
	const series = fresh?.series ?? EMPTY_SERIES;
	const volumes = fresh?.volumes ?? EMPTY_VOLUMES;

	if (loading) {
		return (
			<Box sx={{ display: "flex", justifyContent: "center", py: 10 }}>
				<CircularProgress size={30} thickness={5} />
			</Box>
		);
	}

	if (fresh.error || !library) {
		return (
			<Box sx={{ maxWidth: 800, mx: "auto" }}>
				<Alert severity="error">{fresh.error ?? "La biblioteca no existe o no es accesible."}</Alert>
				<Button component={RouterLink} to="/libraries" startIcon={<ArrowBackIcon />} sx={{ mt: 2 }}>
					VOLVER A BIBLIOTECAS
				</Button>
			</Box>
		);
	}

	const visibleSeries = filterAndSortSeries(series, query, seriesSort, seriesDir);
	const visibleVolumes = filterAndSortMedia(volumes, query, mediaSort, mediaDir);
	const totalPagesCount = volumes.reduce((sum, v) => sum + v.pages, 0);
	const totalSize = volumes.reduce((sum, v) => sum + v.size, 0);

	return (
		<Box>
			<Button
				component={RouterLink}
				to="/libraries"
				startIcon={<ArrowBackIcon />}
				sx={{ color: DS.muted, mb: 2, "&:hover": { color: "#FFFFFF" } }}
			>
				BIBLIOTECAS
			</Button>

			<Box
				sx={{
					position: "relative",
					p: { xs: 2, sm: 3 },
					mb: 3,
					backgroundImage: DS.gradientCard,
					border: `1px solid ${DS.border}`,
				}}
			>
				<HudFrame />

				<Stack
					direction={{ xs: "column", md: "row" }}
					sx={{ alignItems: { md: "center" }, justifyContent: "space-between", gap: 2 }}
				>
					<Box sx={{ minWidth: 0 }}>
						<Stack direction="row" spacing={1.5} sx={{ alignItems: "center" }}>
							<Box
								sx={{
									width: 42,
									height: 42,
									flexShrink: 0,
									display: "flex",
									alignItems: "center",
									justifyContent: "center",
									background: "linear-gradient(145deg, #1C0303 0%, #0D0E12 100%)",
									border: `1px solid ${DS.borderRed}`,
								}}
							>
								<LibraryBooksIcon sx={{ color: DS.redGlow, fontSize: 22 }} />
							</Box>
							<Box sx={{ minWidth: 0 }}>
								<Typography
									component="h1"
									sx={{
										fontFamily: "'Rajdhani', sans-serif",
										fontSize: { xs: "24px", sm: "30px" },
										fontWeight: 700,
										letterSpacing: "1.5px",
										textTransform: "uppercase",
										color: "#FFFFFF",
										lineHeight: 1.1,
									}}
								>
									{library.name}
								</Typography>
								<Typography
									sx={{ fontFamily: "'JetBrains Mono', monospace", fontSize: "11px", color: DS.muted }}
								>
									{library.path}
								</Typography>
							</Box>
						</Stack>

						{library.description && (
							<Typography
								sx={{ fontFamily: "'Inter', sans-serif", fontSize: "13px", color: "#A3ABB8", mt: 1.5, maxWidth: 700 }}
							>
								{library.description}
							</Typography>
						)}

						<Stack direction="row" spacing={1} sx={{ flexWrap: "wrap", gap: 1, mt: 2 }}>
							<Chip size="small" variant="outlined" label={library.status || "READY"} />
							{library.config?.libraryType && (
								<Chip size="small" variant="outlined" label={library.config.libraryType.toUpperCase()} />
							)}
							<Chip
								size="small"
								variant="outlined"
								label={library.lastScannedAt
									? `ESCANEADA ${new Date(library.lastScannedAt).toLocaleDateString("es-ES")}`
									: "NUNCA ESCANEADA"}
							/>
						</Stack>
					</Box>

					<Stack direction="row" spacing={1.5} sx={{ flexShrink: 0 }}>
						<Button
							startIcon={<SyncIcon />}
							onClick={() => {
								void librariesApi.scan(library.id);
							}}
							sx={{
								color: "#A3ABB8",
								border: `1px solid ${DS.border}`,
								backgroundColor: DS.bgSunken,
								"&:hover": { borderColor: DS.redGlow, color: "#FFFFFF" },
							}}
						>
							ESCANEAR
						</Button>
						<Button
							component={RouterLink}
							to="/libraries"
							startIcon={<EditIcon />}
							sx={{
								color: "#A3ABB8",
								border: `1px solid ${DS.border}`,
								backgroundColor: DS.bgSunken,
								"&:hover": { borderColor: DS.redGlow, color: "#FFFFFF" },
							}}
						>
							CONFIGURAR
						</Button>
					</Stack>
				</Stack>

				<Box sx={{ display: "flex", flexWrap: "wrap", gap: 1, mt: 3 }}>
					<Stat label="Series" value={String(series.length)} />
					<Stat label="Tomos" value={String(volumes.length)} />
					<Stat label="Páginas" value={String(totalPagesCount)} />
					<Stat label="Peso" value={formatBytes(totalSize)} />
				</Box>
			</Box>

			<Tabs
				value={tab}
				onChange={(_, next: number) => setTab(next)}
				sx={{
					borderBottom: `1px solid ${DS.border}`,
					mb: 3,
					"& .MuiTab-root": {
						fontFamily: "'Rajdhani', sans-serif",
						fontWeight: 700,
						letterSpacing: "1.5px",
						color: DS.muted,
						"&.Mui-selected": { color: "#FFFFFF" },
					},
					"& .MuiTabs-indicator": { backgroundColor: DS.red, height: 2 },
				}}
			>
				<Tab label={`Series (${String(series.length)})`} />
				<Tab label={`Tomos (${String(volumes.length)})`} />
			</Tabs>

			{tab === 0 ? (
				<>
					<CatalogToolbar
						query={query}
						onQueryChange={setQuery}
						sort={seriesSort}
						onSortChange={setSeriesSort}
						direction={seriesDir}
						onDirectionChange={setSeriesDir}
						options={SERIES_SORTS}
						placeholder="Buscar serie por nombre..."
						shown={visibleSeries.length}
						total={series.length}
					/>
					<Box sx={{ display: "grid", gap: 2.5, gridTemplateColumns: COVER_GRID }}>
						{visibleSeries.map((item) => (
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
				</>
			) : (
				<>
					<CatalogToolbar
						query={query}
						onQueryChange={setQuery}
						sort={mediaSort}
						onSortChange={setMediaSort}
						direction={mediaDir}
						onDirectionChange={setMediaDir}
						options={MEDIA_SORTS}
						placeholder="Buscar tomo por nombre..."
						shown={visibleVolumes.length}
						total={volumes.length}
					/>
					<Box sx={{ display: "grid", gap: 2.5, gridTemplateColumns: COVER_GRID }}>
						{visibleVolumes.map((item) => (
							<MediaCard
								key={item.id}
								to={`/media/${item.id}`}
								title={mediaTitle(item)}
								subtitle={mediaSubtitle(item)}
								coverUrl={mediaApi.thumbnailUrl(item.id)}
								progress={readProgress(item)}
								width="100%"
							/>
						))}
					</Box>
				</>
			)}
		</Box>
	);
}
