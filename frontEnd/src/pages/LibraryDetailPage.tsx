import {
	Alert,
	Box,
	Button,
	Chip,
	CircularProgress,
	LinearProgress,
	Stack,
	Tab,
	Tabs,
	Typography,
} from "@mui/material";
import ArrowBackIcon from "@mui/icons-material/ArrowBack";
import SyncIcon from "@mui/icons-material/Sync";
import EditIcon from "@mui/icons-material/Edit";
import LibraryBooksIcon from "@mui/icons-material/LibraryBooks";
import { useEffect, useState } from "react";
import { Link as RouterLink, useParams } from "react-router-dom";
import HudFrame from "../components/HudFrame";
import MissingEntriesDialog from "../components/MissingEntriesDialog";
import { useScanProgress, formatDuration, scanLabel } from "../catalog/useScanProgress";
import MediaCard from "../components/MediaCard";
import CatalogToolbar, { type SortDirection } from "../components/CatalogToolbar";
import {
	librariesApi,
	mediaApi,
	seriesApi,
	MAX_PAGE_SIZE,
	type ScanStatus,
	type MissingReport,
	filesystemApi,
	type PreviewSeries,
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
			<Typography sx={{ fontFamily: "'JetBrains Mono', monospace", fontSize: "16px", color: "var(--ds-text-strong)" }}>
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
	const [reloadToken, setReloadToken] = useState(0);

	// El token sube al lanzar un escaneo: es lo que hace que se vuelva a preguntar y,
	// si hay algo en marcha, se abra el flujo.
	const progress = useScanProgress(id, reloadToken);

	// La clave lleva el numero de series ya hechas. El progreso baja por SSE, pero las
	// series y los tomos se leen de la base de datos, asi que la recarga se engancha a que
	// haya algo nuevo que enseñar en vez de a un reloj. El estado final cuenta aparte:
	// el ultimo lote de tomos entra despues del evento de la ultima serie.
	const requestKey = [
		id,
		String(reloadToken),
		String(progress?.completedSeries ?? 0),
		String(progress?.completedMedia ?? 0),
		progress?.finished ? "done" : "live",
	].join("#");

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
				setLoaded({ key: requestKey, library, series, volumes: perSeries.flat() });
			}
		};

		void load().catch((err: unknown) => {
			if (mounted) {
				setLoaded({ key: requestKey, error: err instanceof Error ? err.message : "No se pudo cargar la biblioteca." });
			}
		});

		return () => {
			mounted = false;
		};
	}, [id, requestKey]);

	// Mientras llega la recarga se sigue enseñando lo anterior. Vaciar la pantalla en cada
	// lote del escaneo la dejaba parpadeando: la rejilla desaparecia y volvia varias veces
	// por serie. Solo la primera carga, cuando no hay nada que enseñar, muestra el giro.
	const shown = loaded;
	const loading = loaded === null;
	const library = shown?.library ?? null;
	const series = shown?.series ?? EMPTY_SERIES;
	const volumes = shown?.volumes ?? EMPTY_VOLUMES;
	// El estado de escaneo vive en memoria, no en la base de datos: si el proceso muere,
	// la verdad es que ya no escanea nada, y una fila persistida mentiria para siempre.
	const scanning = progress !== null && !progress.finished;

	// Se relee cuando termina un escaneo: es cuando aparecen las entradas perdidas.
	const [missing, setMissing] = useState<MissingReport | null>(null);
	const [missingOpen, setMissingOpen] = useState(false);
	const [purging, setPurging] = useState(false);

	useEffect(() => {
		let cancelled = false;
		librariesApi.missing(id).then(
			(report) => {
				if (!cancelled) setMissing(report);
			},
			() => {
				if (!cancelled) setMissing(null);
			},
		);

		return () => {
			cancelled = true;
		};
	}, [id, requestKey]);

	const missingCount = (missing?.series.length ?? 0) + (missing?.orphanVolumes ?? 0);
	const pending = usePendingSeries(library, scanning, series);

	if (loading) {
		return (
			<Box sx={{ display: "flex", justifyContent: "center", py: 10 }}>
				<CircularProgress size={30} thickness={5} />
			</Box>
		);
	}

	if (shown?.error !== undefined || !library) {
		return (
			<Box sx={{ maxWidth: 800, mx: "auto" }}>
				<Alert severity="error">{shown?.error ?? "La biblioteca no existe o no es accesible."}</Alert>
				<Button component={RouterLink} to="/libraries" startIcon={<ArrowBackIcon />} sx={{ mt: 2 }}>
					VOLVER A BIBLIOTECAS
				</Button>
			</Box>
		);
	}

	const visibleSeries = filterAndSortSeries(series, query, seriesSort, seriesDir);
	const visibleVolumes = filterAndSortMedia(volumes, query, mediaSort, mediaDir);

	// Mientras se indexa, la rejilla de tomos enseña tantos huecos como falten para el total
	// que anuncia el escaner, y el barrido va en el primero: el que se esta midiendo ahora.
	const indexingMedia = scanning ? progress?.currentMedia : undefined;
	const pendingVolumes =
		scanning && progress !== null && progress.totalMedia > 0
			? Math.max(0, progress.totalMedia - visibleVolumes.length)
			: 0;
	const totalPagesCount = volumes.reduce((sum, v) => sum + v.pages, 0);
	const totalSize = volumes.reduce((sum, v) => sum + v.size, 0);

	return (
		<Box>
			<Button
				component={RouterLink}
				to="/libraries"
				startIcon={<ArrowBackIcon />}
				sx={{ color: DS.muted, mb: 2, "&:hover": { color: "var(--ds-text-strong)" } }}
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
				{scanning && <Box className="ds-scanline" />}
				{scanning && progress && <ScanBanner progress={progress} />}

				{!scanning && missingCount > 0 && (
					<Alert
						severity="warning"
						sx={{ mt: 2 }}
						action={
							<Button color="inherit" size="small" onClick={() => { setMissingOpen(true); }}>
								REVISAR
							</Button>
						}
					>
						{missing?.series.length ?? 0} series y {missing?.totalVolumes ?? 0} tomos siguen en el índice pero
						ya no están en el disco.
					</Alert>
				)}

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
									background: "linear-gradient(145deg, var(--ds-red-deep) 0%, var(--ds-bg-deep) 100%)",
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
										color: "var(--ds-text-strong)",
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
								sx={{ fontFamily: "'Inter', sans-serif", fontSize: "13px", color: "var(--ds-text-2)", mt: 1.5, maxWidth: 700 }}
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
							disabled={scanning}
							onClick={() => {
								void librariesApi.scan(library.id).then(() => {
									setReloadToken((prev) => prev + 1);
								});
							}}
							sx={{
								color: "var(--ds-text-2)",
								border: `1px solid ${DS.border}`,
								backgroundColor: DS.bgSunken,
								"&:hover": { borderColor: DS.redGlow, color: "var(--ds-text-strong)" },
							}}
						>
							ESCANEAR
						</Button>
						<Button
							component={RouterLink}
							to="/libraries"
							startIcon={<EditIcon />}
							sx={{
								color: "var(--ds-text-2)",
								border: `1px solid ${DS.border}`,
								backgroundColor: DS.bgSunken,
								"&:hover": { borderColor: DS.redGlow, color: "var(--ds-text-strong)" },
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
						"&.Mui-selected": { color: "var(--ds-text-strong)" },
					},
					"& .MuiTabs-indicator": { backgroundColor: DS.red, height: 2 },
				}}
			>
				<Tab label={`Series (${String(series.length)})`} />
				<Tab label={`Tomos (${String(volumes.length)})`} />
			</Tabs>

			{missingOpen && missing && (
				<MissingEntriesDialog
					report={missing}
					working={purging}
					onClose={() => { setMissingOpen(false); }}
					onPurge={() => {
						setPurging(true);
						librariesApi
							.purgeMissing(id)
							.then(() => {
								setMissingOpen(false);
								setReloadToken((prev) => prev + 1);
							})
							.finally(() => {
								setPurging(false);
							})
							.catch(() => undefined);
					}}
				/>
			)}

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
								// Solo la que el escaner esta leyendo ahora: encenderlas todas
								// diria menos, porque no señalaria donde va.
								scanning={scanning && item.name === progress?.currentSeries}
							/>
						))}

						{/* Huecos de lo que el escaner todavia no ha creado. Salen del mismo ensayo
						    que la vista previa del formulario, asi que la rejilla no esta vacia
						    mientras se indexa. */}
						{pending.map((item) => (
							<PendingCard key={item.path} name={item.name} volumeCount={item.volumeCount} />
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
								scanning={item.name === indexingMedia}
							/>
						))}

						{Array.from({ length: pendingVolumes }, (_, slot) => (
							<PendingVolume
								key={`tomo-pendiente-${String(slot)}`}
								label={`TOMO ${String(visibleVolumes.length + slot + 1).padStart(2, "0")}`}
								active={slot === 0}
							/>
						))}
					</Box>
				</>
			)}
		</Box>
	);
}

/** Lo que el escaner esta haciendo ahora mismo, con lo que falta si se puede estimar. */
function ScanBanner({ progress }: Readonly<{ progress: ScanStatus }>) {
	const known = progress.totalSeries > 0;

	return (
		<Box sx={{ mt: 2, pt: 2, borderTop: `1px solid ${DS.borderSoft}` }}>
			<Stack
				direction="row"
				spacing={1}
				sx={{ alignItems: "baseline", justifyContent: "space-between", flexWrap: "wrap", gap: 1, mb: 1 }}
			>
				<Typography
					noWrap
					sx={{ fontFamily: "'Rajdhani', sans-serif", fontWeight: 700, fontSize: "13px", letterSpacing: "1px", color: DS.redGlow }}
				>
					{scanLabel(progress)}
				</Typography>

				<Typography sx={{ fontFamily: "'JetBrains Mono', monospace", fontSize: "11px", color: DS.muted }}>
					{known
						? `${String(progress.completedSeries)} / ${String(progress.totalSeries)} series`
						: "explorando el disco"}
					{progress.etaSeconds !== undefined && ` · quedan ~${formatDuration(progress.etaSeconds)}`}
					{progress.etaSeconds === undefined && !progress.queued && (
						<>
							{" · calculando ETA"}
							<Box component="span" className="ds-dots">
								<span>.</span>
								<span>.</span>
								<span>.</span>
							</Box>
						</>
					)}
				</Typography>
			</Stack>

			<LinearProgress
				variant={known ? "determinate" : "indeterminate"}
				value={progress.percentage}
				sx={{ height: 4 }}
			/>
		</Box>
	);
}

/**
 * Series que el ensayo del disco ve pero la base de datos todavia no. Se pide una sola vez
 * al arrancar el escaneo: recorrer el disco cuesta y el resultado no cambia mientras dura.
 */
function usePendingSeries(library: LibraryItem | null, scanning: boolean, existing: SeriesItem[]) {
	const [expected, setExpected] = useState<{ key: string; series: PreviewSeries[] } | null>(null);
	const path = library?.path ?? "";
	const pattern = library?.config?.libraryPattern ?? "SeriesBased";
	const key = scanning ? `${path}#${pattern}` : "";

	useEffect(() => {
		if (!key || !path) return;

		let cancelled = false;
		filesystemApi.preview(path, pattern).then(
			(data) => {
				if (!cancelled) setExpected({ key, series: data.series });
			},
			() => {
				if (!cancelled) setExpected({ key, series: [] });
			},
		);

		return () => {
			cancelled = true;
		};
	}, [key, path, pattern]);

	if (!scanning || expected?.key !== key) return EMPTY_PENDING;

	const done = new Set(existing.map((item) => item.name));

	return expected.series.filter((item) => !done.has(item.name));
}

const EMPTY_PENDING: PreviewSeries[] = [];

/** Hueco de una serie aun sin indexar: sin portada porque todavia no se ha abierto ningun tomo. */
/**
 * Hueco de un tomo que el escaner aun no ha creado. El barrido solo va en el que se esta
 * midiendo: encendidos todos, la rejilla no diria por donde va.
 */
function PendingVolume({ label, active }: Readonly<{ label: string; active: boolean }>) {
	return (
		<Box sx={{ opacity: active ? 0.9 : 0.45 }}>
			<Box
				sx={{
					position: "relative",
					aspectRatio: "2 / 3",
					backgroundImage: DS.gradientCard,
					border: `1px dashed ${active ? DS.borderRed : DS.border}`,
					display: "flex",
					alignItems: "center",
					justifyContent: "center",
				}}
			>
				{active && <Box className="ds-scanline" />}
				<LibraryBooksIcon sx={{ fontSize: 26, color: active ? DS.borderRed : DS.border }} />
			</Box>
			<Typography
				noWrap
				sx={{
					mt: 1,
					fontFamily: "'JetBrains Mono', monospace",
					fontSize: "11px",
					color: active ? DS.redGlow : DS.subtle,
				}}
			>
				{label}
			</Typography>
		</Box>
	);
}

function PendingCard({ name, volumeCount }: Readonly<{ name: string; volumeCount: number }>) {
	return (
		<Box sx={{ opacity: 0.55 }}>
			<Box
				sx={{
					position: "relative",
					aspectRatio: "2 / 3",
					backgroundImage: DS.gradientCard,
					border: `1px dashed ${DS.border}`,
					display: "flex",
					alignItems: "center",
					justifyContent: "center",
				}}
			>
				<Box className="ds-scanline" />
				<LibraryBooksIcon sx={{ fontSize: 30, color: DS.borderRed }} />
			</Box>
			<Typography
				noWrap
				sx={{ mt: 1, fontFamily: "'Rajdhani', sans-serif", fontWeight: 700, fontSize: "13px", color: DS.muted }}
			>
				{name}
			</Typography>
			<Typography sx={{ fontFamily: "'JetBrains Mono', monospace", fontSize: "11px", color: DS.subtle }}>
				{volumeCount} TOMOS · EN COLA
			</Typography>
		</Box>
	);
}
