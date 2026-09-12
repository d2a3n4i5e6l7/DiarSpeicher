import {
	Alert,
	Box,
	Button,
	Chip,
	CircularProgress,
	Collapse,
	IconButton,
	Stack,
	Tab,
	Tabs,
	Tooltip,
	Typography,
} from "@mui/material";
import PlayArrowIcon from "@mui/icons-material/PlayArrow";
import ExpandMoreIcon from "@mui/icons-material/ExpandMore";
import ArrowBackIcon from "@mui/icons-material/ArrowBack";
import TravelExploreIcon from "@mui/icons-material/TravelExplore";
import LinkOffIcon from "@mui/icons-material/LinkOff";
import EditIcon from "@mui/icons-material/Edit";
import ImageIcon from "@mui/icons-material/Image";
import { useEffect, useMemo, useState } from "react";
import { Link as RouterLink, useParams } from "react-router-dom";
import HudFrame from "../components/HudFrame";
import MediaCard from "../components/MediaCard";
import MetadataMatchDialog from "../components/MetadataMatchDialog";
import RenameSeriesDialog from "../components/RenameSeriesDialog";
import SeriesCoverDialog from "../components/SeriesCoverDialog";
import { mediaApi, metadataApi, seriesApi, type MediaItem, type SeriesItem } from "../api/endpoints";
import {
	estimatedMinutes,
	formatBytes,
	formatDuration,
	mediaSubtitle,
	mediaTitle,
	readProgress,
} from "../catalog/mediaHelpers";
import { COVER_GRID, READABLE_WIDTH } from "../catalog/layout";
import { DS } from "../theme";

/**
 * Campo del tablón inferior. Un campo sin dato no se pinta: media ficha llena de guiones
 * cuesta más de leer que media ficha corta.
 */
function DataField({ label, value }: Readonly<{ label: string; value: string | number | null | undefined }>) {
	if (value === null || value === undefined || value === "" || value === 0) return null;

	return (
		<Box sx={{ mb: 2.5 }}>
			<Typography
				sx={{
					fontFamily: "'Rajdhani', sans-serif",
					fontSize: "12px",
					fontWeight: 700,
					letterSpacing: "2px",
					textTransform: "uppercase",
					color: DS.redLight,
					mb: 0.5,
				}}
			>
				{label}
			</Typography>
			<Typography sx={{ fontFamily: "'Inter', sans-serif", fontSize: "13px", color: DS.platinum }}>
				{String(value)}
			</Typography>
		</Box>
	);
}

/** Referencia estable: un array nuevo por render rompería las dependencias del useMemo. */
const EMPTY_VOLUMES: MediaItem[] = [];

interface Result {
	key: string;
	series?: SeriesItem;
	volumes?: MediaItem[];
	error?: string;
}

/** Un chip sin dato no se renderiza: la cabecera no debe llenarse de guiones. */
function MetaChip({ label, value }: Readonly<{ label: string; value: string | number | null | undefined }>) {
	if (value === null || value === undefined || value === "" || value === 0) return null;
	return (
		<Chip
			size="small"
			variant="outlined"
			label={`${label} ${String(value)}`.trim()}
			sx={{ fontFamily: "'JetBrains Mono', monospace" }}
		/>
	);
}

function DetailRow({ label, value }: Readonly<{ label: string; value: string | null | undefined }>) {
	if (!value) return null;
	return (
		<Box sx={{ display: "flex", gap: 2, py: 1, borderBottom: `1px solid ${DS.borderSoft}` }}>
			<Typography
				sx={{
					fontFamily: "'Rajdhani', sans-serif",
					fontWeight: 700,
					fontSize: "12px",
					letterSpacing: "1.5px",
					textTransform: "uppercase",
					color: DS.muted,
					minWidth: 150,
					flexShrink: 0,
				}}
			>
				{label}
			</Typography>
			<Typography
				sx={{ fontFamily: "'JetBrains Mono', monospace", fontSize: "12px", color: DS.platinum, wordBreak: "break-all" }}
			>
				{value}
			</Typography>
		</Box>
	);
}

export default function SeriesDetailPage() {
	const { id = "" } = useParams();
	const [tab, setTab] = useState(0);
	const [summaryOpen, setSummaryOpen] = useState(false);
	const [matchOpen, setMatchOpen] = useState(false);
	const [renameOpen, setRenameOpen] = useState(false);
	const [coverOpen, setCoverOpen] = useState(false);
	// Se incrementa tras emparejar o revertir: fuerza a releer la ficha con lo nuevo.
	const [reloadToken, setReloadToken] = useState(0);

	// El resultado se guarda con el id que lo pidió: navegar de una serie a otra
	// descarta solo la respuesta anterior, sin reiniciar estado en un efecto.
	const [result, setResult] = useState<Result | null>(null);
	const requestKey = `${id}#${String(reloadToken)}`;

	useEffect(() => {
		let mounted = true;
		Promise.all([seriesApi.get(id), seriesApi.media(id, 0, 100)])
			.then(([detail, media]) => {
				// La API ya devuelve los tomos en su orden natural: no se reordena aquí.
				if (mounted) setResult({ key: requestKey, series: detail, volumes: media.data });
			})
			.catch((err: unknown) => {
				if (mounted) {
					setResult({ key: requestKey, error: err instanceof Error ? err.message : "No se pudo cargar la serie." });
				}
			});

		return () => {
			mounted = false;
		};
	}, [id, requestKey]);

	const fresh = result?.key === requestKey ? result : null;
	const loading = fresh === null;
	const series = fresh?.series ?? null;
	const volumes = fresh?.volumes ?? EMPTY_VOLUMES;
	const error = fresh?.error ?? null;

	const stats = useMemo(() => {
		const totalPages = volumes.reduce((sum, v) => sum + v.pages, 0);
		const totalSize = volumes.reduce((sum, v) => sum + v.size, 0);
		const readCount = volumes.filter((v) => v.isCompleted).length;
		const percent = volumes.length === 0 ? 0 : Math.round((readCount / volumes.length) * 100);
		// El primer tomo sin terminar es por donde se continúa.
		const nextVolume = volumes.find((v) => !v.isCompleted) ?? volumes[0];
		return { totalPages, totalSize, readCount, percent, nextVolume };
	}, [volumes]);

	if (loading) {
		return (
			<Box sx={{ display: "flex", justifyContent: "center", py: 10 }}>
				<CircularProgress size={30} thickness={5} />
			</Box>
		);
	}

	const external = series?.metadata ?? null;

	const unmatch = async () => {
		await metadataApi.unmatch(id).catch(() => undefined);
		setReloadToken((token) => token + 1);
	};

	if (error || !series) {
		return (
			<Box sx={{ maxWidth: 800, mx: "auto" }}>
				<Alert severity="error">{error ?? "La serie no existe o no es accesible."}</Alert>
				<Button component={RouterLink} to="/series" startIcon={<ArrowBackIcon />} sx={{ mt: 2 }}>
					VOLVER A SERIES
				</Button>
			</Box>
		);
	}

	const summary = external?.summary?.trim() || series.description?.trim();

	// Lo unico del tablon que no viene del catalogo: sale de los ficheros que hay en disco.
	const volumeFormats = [...new Set(volumes.map((v) => v.extension.replace(/^\./, "").toUpperCase()))]
		.sort((a, b) => a.localeCompare(b))
		.join(", ");

	// El volcado trae el último volumen publicado, y es de lo más útil que aporta: dice
	// si la colección está completa, que no hay forma de deducir de los ficheros.
	const completeness =
		external?.finalVolume && external.finalVolume > 0
			? {
					label: `${String(volumes.length)} DE ${String(external.finalVolume)}`,
					complete: volumes.length >= external.finalVolume,
				}
			: null;

	return (
		<Box>
			<Button
				component={RouterLink}
				to="/series"
				startIcon={<ArrowBackIcon />}
				sx={{ color: DS.muted, mb: 2, "&:hover": { color: "#FFFFFF" } }}
			>
				SERIES
			</Button>

			<Box
				sx={{
					position: "relative",
					display: "flex",
					flexDirection: { xs: "column", sm: "row" },
					gap: 3,
					p: { xs: 2, sm: 3 },
					mb: 3,
					backgroundImage: DS.gradientCard,
					border: `1px solid ${DS.border}`,
				}}
			>
				<HudFrame />

				<Box sx={{ width: { xs: "100%", sm: 220 }, flexShrink: 0 }}>
					<MediaCard
						to={stats.nextVolume ? `/read/${stats.nextVolume.id}` : `/series/${series.id}`}
						title=""
						coverUrl={seriesApi.thumbnailUrl(series.id, series.coverUpdatedAt)}
						progress={stats.percent}
						width="100%"
					/>
				</Box>

				<Box sx={{ minWidth: 0, flexGrow: 1 }}>
					<Stack direction="row" spacing={1} sx={{ alignItems: "center" }}>
						<Typography
							component="h1"
							sx={{
								fontFamily: "'Rajdhani', sans-serif",
								fontSize: { xs: "24px", sm: "30px" },
								fontWeight: 700,
								letterSpacing: "1.5px",
								textTransform: "uppercase",
								color: "#FFFFFF",
								lineHeight: 1.15,
							}}
						>
							{series.name}
						</Typography>
						<Tooltip title="Renombrar serie">
							<IconButton
								size="small"
								onClick={() => setRenameOpen(true)}
								sx={{
									color: DS.muted,
									border: `1px solid ${DS.border}`,
									borderRadius: 0,
									"&:hover": { color: "#FFFFFF", borderColor: DS.red },
								}}
							>
								<EditIcon fontSize="small" />
							</IconButton>
						</Tooltip>
						<Tooltip title="Cambiar portada">
							<IconButton
								size="small"
								onClick={() => setCoverOpen(true)}
								sx={{
									color: DS.muted,
									border: `1px solid ${DS.border}`,
									borderRadius: 0,
									"&:hover": { color: "#FFFFFF", borderColor: DS.red },
								}}
							>
								<ImageIcon fontSize="small" />
							</IconButton>
						</Tooltip>
					</Stack>

					<Stack direction="row" spacing={1} sx={{ flexWrap: "wrap", gap: 1, mt: 1.5 }}>
						<MetaChip label="TOMOS" value={series.mediaCount || volumes.length} />
						<MetaChip label="PÁGS" value={stats.totalPages} />
						<MetaChip
							label="LECTURA"
							value={stats.totalPages > 0 ? formatDuration(estimatedMinutes(stats.totalPages)) : null}
						/>
						<MetaChip label="PESO" value={stats.totalSize > 0 ? formatBytes(stats.totalSize) : null} />
						<MetaChip label="" value={external?.status ?? series.status ?? null} />
						<MetaChip label="" value={external?.year ?? null} />
						<MetaChip label="EDITORIAL" value={external?.publisher ?? null} />
						<MetaChip label="AUTOR" value={external?.writers ?? null} />
						<MetaChip label="" value={external?.genres ?? null} />
						{completeness && (
							<Chip
								size="small"
								label={completeness.label}
								sx={{
									fontFamily: "'JetBrains Mono', monospace",
									backgroundColor: completeness.complete ? "rgba(34, 197, 94, 0.12)" : "rgba(245, 158, 11, 0.15)",
									color: completeness.complete ? "#4ADE80" : "#FBBF24",
									border: `1px solid ${completeness.complete ? "#14532D" : "#78350F"}`,
								}}
							/>
						)}
						</Stack>

						{external && (
							<Stack direction="row" spacing={1} sx={{ alignItems: "center", mt: 1.5 }}>
								<Chip
									size="small"
									label={`SEGÚN ${(external.source ?? "CATÁLOGO EXTERNO").toUpperCase()}`}
									sx={{ backgroundColor: "rgba(194, 24, 24, 0.15)", color: DS.redLight, border: `1px solid ${DS.redDark}` }}
								/>
								<Button
									size="small"
									startIcon={<LinkOffIcon />}
									onClick={() => {
										void unmatch();
									}}
									sx={{ color: DS.muted, "&:hover": { color: DS.redGlow } }}
								>
									QUITAR VÍNCULO
								</Button>
							</Stack>
						)}

					<Box sx={{ mt: 2.5 }}>
						<Typography sx={{ fontFamily: "'JetBrains Mono', monospace", fontSize: "11px", color: DS.subtle, mb: 0.75 }}>
							PROGRESO {String(stats.percent)}% · {String(stats.readCount)}/{String(volumes.length)} TOMOS
						</Typography>
						<Box sx={{ height: 4, backgroundColor: DS.redDeep, width: "100%" }}>
							<Box sx={{ height: "100%", width: `${String(stats.percent)}%`, backgroundColor: DS.redGlow }} />
						</Box>
					</Box>

					<Stack direction="row" spacing={1.5} sx={{ mt: 2.5, flexWrap: "wrap", gap: 1.5 }}>
						{stats.nextVolume && (
							<Button
								component={RouterLink}
								to={`/read/${stats.nextVolume.id}`}
								variant="contained"
								startIcon={<PlayArrowIcon />}
								className="btn-tactical"
								sx={{ background: DS.red, borderColor: DS.redGlow, color: "#FFFFFF", px: 3 }}
							>
								CONTINUAR POR {mediaTitle(stats.nextVolume).slice(0, 28).toUpperCase()}
							</Button>
						)}
						<Button
							startIcon={<TravelExploreIcon />}
							onClick={() => setMatchOpen(true)}
							sx={{
								color: "#A3ABB8",
								border: `1px solid ${DS.border}`,
								backgroundColor: DS.bgSunken,
								"&:hover": { borderColor: DS.redGlow, color: "#FFFFFF" },
							}}
						>
							{external ? "CAMBIAR EMPAREJADO" : "BUSCAR METADATA"}
						</Button>
						</Stack>

					{summary && (
						<Box sx={{ mt: 2.5 }}>
							<Collapse in={summaryOpen} collapsedSize={44}>
								<Typography sx={{ fontFamily: "'Inter', sans-serif", fontSize: "13px", color: "#A3ABB8", lineHeight: 1.7 }}>
									{summary}
								</Typography>
							</Collapse>
							<Button
								size="small"
								endIcon={
									<ExpandMoreIcon
										sx={{ transform: summaryOpen ? "rotate(180deg)" : "none", transition: "transform 0.2s ease" }}
									/>
								}
								onClick={() => setSummaryOpen((open) => !open)}
								sx={{ color: DS.redGlow, mt: 0.5 }}
							>
								{summaryOpen ? "PLEGAR" : "LEER MÁS"}
							</Button>
						</Box>
					)}

					<Box
						sx={{
							mt: 3,
							pt: 3,
							borderTop: `1px solid ${DS.borderSoft}`,
							display: "grid",
							gap: { xs: 0, sm: 4 },
							gridTemplateColumns: { xs: "1fr", sm: "repeat(auto-fit, minmax(220px, 1fr))" },
						}}
					>
						<Box>
							<DataField label="Autores" value={external?.writers} />
							<DataField label="Géneros" value={external?.genres} />
							<DataField label="Formato" value={volumeFormats} />
						</Box>
						<Box>
							<DataField label="Publicación" value={external?.status} />
							<DataField label="Editorial" value={external?.publisher} />
							<DataField label="Año" value={external?.year} />
						</Box>
						<Box>
							<DataField label="Tipo" value={external?.type} />
							<DataField label="Capítulos" value={external?.totalChapters} />
							<DataField
								label="Volúmenes"
								value={external?.finalVolume ? `${String(volumes.length)} de ${String(external.finalVolume)}` : null}
							/>
						</Box>
					</Box>
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
				<Tab label={`Tomos (${String(volumes.length)})`} />
				<Tab label="Detalles" />
			</Tabs>

			{tab === 0 && (
				<Box sx={{ display: "grid", gap: 2.5, gridTemplateColumns: COVER_GRID }}>
					{volumes.map((volume) => (
						<MediaCard
							key={volume.id}
							to={`/media/${volume.id}`}
							title={mediaTitle(volume)}
							subtitle={mediaSubtitle(volume)}
							coverUrl={mediaApi.thumbnailUrl(volume.id)}
							progress={readProgress(volume)}
							badge={volume.metadata?.number !== undefined ? `#${String(volume.metadata.number)}` : undefined}
							width="100%"
						/>
					))}
				</Box>
			)}

			{tab === 1 && (
				<Box sx={{ maxWidth: READABLE_WIDTH }}>
					<DetailRow label="ID de serie" value={series.id} />
					<DetailRow label="Ruta en disco" value={series.path} />
					<DetailRow label="Biblioteca" value={series.libraryId} />
					<DetailRow label="Estado" value={series.status} />
					<DetailRow label="Tomos indexados" value={String(volumes.length)} />
					<DetailRow label="Páginas totales" value={stats.totalPages > 0 ? String(stats.totalPages) : null} />
					<DetailRow label="Peso en disco" value={stats.totalSize > 0 ? formatBytes(stats.totalSize) : null} />
					<DetailRow label="Catálogo externo" value={external?.source} />
					<DetailRow label="Id externo" value={external?.externalId ? String(external.externalId) : null} />
					<DetailRow label="Ficha de origen" value={external?.link} />
							</Box>
						)}

						<MetadataMatchDialog
							open={matchOpen}
							seriesId={series.id}
							seriesName={series.name}
							onClose={() => setMatchOpen(false)}
							onMatched={() => setReloadToken((token) => token + 1)}
						/>

						{renameOpen && (
							<RenameSeriesDialog
								open
								seriesId={series.id}
								currentName={series.name}
								currentDescription={series.description}
								onClose={() => setRenameOpen(false)}
								onSaved={() => setReloadToken((token) => token + 1)}
							/>
						)}

						{coverOpen && (
							<SeriesCoverDialog
								open
								seriesId={series.id}
								onClose={() => setCoverOpen(false)}
								onChanged={() => setReloadToken((token) => token + 1)}
							/>
						)}
					</Box>
	);
}
