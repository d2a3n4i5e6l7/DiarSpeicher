import { Alert, Box, Button, Chip, CircularProgress, Stack, Typography } from "@mui/material";
import PlayArrowIcon from "@mui/icons-material/PlayArrow";
import DownloadIcon from "@mui/icons-material/Download";
import ArrowBackIcon from "@mui/icons-material/ArrowBack";
import { useEffect, useState } from "react";
import { Link as RouterLink, useParams } from "react-router-dom";
import HudFrame from "../components/HudFrame";
import MediaCard from "../components/MediaCard";
import { mediaApi, seriesApi, type MediaItem, type SeriesItem } from "../api/endpoints";
import {
	estimatedMinutes,
	formatBytes,
	formatDuration,
	mediaTitle,
	readProgress,
} from "../catalog/mediaHelpers";
import { READABLE_WIDTH } from "../catalog/layout";
import { DS } from "../theme";

interface Result {
	key: string;
	media?: MediaItem;
	series?: SeriesItem | null;
	error?: string;
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

export default function MediaDetailPage() {
	const { id = "" } = useParams();
	// El resultado se guarda con el id que lo pidió, de modo que al saltar de un tomo
	// a otro se deriva si lo que hay en pantalla corresponde a la ruta actual.
	const [result, setResult] = useState<Result | null>(null);

	useEffect(() => {
		let mounted = true;

		const load = async () => {
			const item = await mediaApi.get(id);
			// La serie es contexto, no contenido: si falla, la ficha sigue sirviendo.
			const parent = item.seriesId ? await seriesApi.get(item.seriesId).catch(() => null) : null;
			if (mounted) setResult({ key: id, media: item, series: parent });
		};

		void load().catch((err: unknown) => {
			if (mounted) {
				setResult({ key: id, error: err instanceof Error ? err.message : "No se pudo cargar el tomo." });
			}
		});

		return () => {
			mounted = false;
		};
	}, [id]);

	const fresh = result?.key === id ? result : null;
	const loading = fresh === null;
	const media = fresh?.media ?? null;
	const series = fresh?.series ?? null;
	const error = fresh?.error ?? null;

	if (loading) {
		return (
			<Box sx={{ display: "flex", justifyContent: "center", py: 10 }}>
				<CircularProgress size={30} thickness={5} />
			</Box>
		);
	}

	if (error || !media) {
		return (
			<Box sx={{ maxWidth: 800, mx: "auto" }}>
				<Alert severity="error">{error ?? "El tomo no existe o no es accesible."}</Alert>
				<Button component={RouterLink} to="/" startIcon={<ArrowBackIcon />} sx={{ mt: 2 }}>
					VOLVER AL ARCHIVO
				</Button>
			</Box>
		);
	}

	const progress = readProgress(media);
	const resumePage = media.currentPage && media.currentPage > 1 ? media.currentPage : 1;
	const summary = media.metadata?.summary?.trim();

	return (
		<Box>
			<Button
				component={RouterLink}
				to={series ? `/series/${series.id}` : "/"}
				startIcon={<ArrowBackIcon />}
				sx={{ color: DS.muted, mb: 2, "&:hover": { color: "#FFFFFF" } }}
			>
				{series ? series.name.toUpperCase() : "ARCHIVO"}
			</Button>

			<Box
				sx={{
					position: "relative",
					display: "flex",
					flexDirection: { xs: "column", sm: "row" },
					gap: 3,
					p: { xs: 2, sm: 3 },
					backgroundImage: DS.gradientCard,
					border: `1px solid ${DS.border}`,
				}}
			>
				<HudFrame />

				<Box sx={{ width: { xs: "100%", sm: 200 }, flexShrink: 0 }}>
					<MediaCard
						to={`/read/${media.id}`}
						title=""
						coverUrl={mediaApi.thumbnailUrl(media.id)}
						progress={progress}
						width="100%"
					/>
				</Box>

				<Box sx={{ minWidth: 0, flexGrow: 1 }}>
					<Typography
						component="h1"
						sx={{
							fontFamily: "'Rajdhani', sans-serif",
							fontSize: { xs: "22px", sm: "28px" },
							fontWeight: 700,
							letterSpacing: "1.5px",
							textTransform: "uppercase",
							color: "#FFFFFF",
							lineHeight: 1.15,
						}}
					>
						{mediaTitle(media)}
					</Typography>

					<Stack direction="row" spacing={1} sx={{ flexWrap: "wrap", gap: 1, mt: 1.5 }}>
						<Chip size="small" variant="outlined" label={media.extension.replace(/^\./, "").toUpperCase()} />
						<Chip size="small" variant="outlined" label={`${String(media.pages)} PÁGS`} />
						<Chip size="small" variant="outlined" label={formatBytes(media.size)} />
						<Chip size="small" variant="outlined" label={formatDuration(estimatedMinutes(media.pages))} />
						{media.isCompleted && <Chip size="small" label="TERMINADO" color="primary" />}
					</Stack>

					{summary && (
						<Typography
							sx={{ fontFamily: "'Inter', sans-serif", fontSize: "13px", color: "#A3ABB8", lineHeight: 1.7, mt: 2 }}
						>
							{summary}
						</Typography>
					)}

					<Stack direction="row" spacing={1.5} sx={{ mt: 3, flexWrap: "wrap", gap: 1.5 }}>
						<Button
							component={RouterLink}
							to={`/read/${media.id}`}
							variant="contained"
							startIcon={<PlayArrowIcon />}
							className="btn-tactical"
							sx={{ background: DS.red, borderColor: DS.redGlow, color: "#FFFFFF", px: 3 }}
						>
							{progress > 0 ? `REANUDAR EN P${String(resumePage)}` : "EMPEZAR LECTURA"}
						</Button>
						<Button
							component="a"
							href={mediaApi.fileUrl(media.id)}
							download
							startIcon={<DownloadIcon />}
							sx={{
								color: "#A3ABB8",
								border: `1px solid ${DS.border}`,
								backgroundColor: DS.bgSunken,
								"&:hover": { borderColor: DS.redGlow, color: "#FFFFFF" },
							}}
						>
							DESCARGAR
						</Button>
					</Stack>
				</Box>
			</Box>

			<Box sx={{ mt: 4, maxWidth: READABLE_WIDTH }}>
				<Typography
					component="h2"
					sx={{
						fontFamily: "'Rajdhani', sans-serif",
						fontSize: "18px",
						fontWeight: 700,
						letterSpacing: "2px",
						textTransform: "uppercase",
						color: "#FFFFFF",
						mb: 1.5,
					}}
				>
					Detalles técnicos
				</Typography>
				<DetailRow label="ID" value={media.id} />
				<DetailRow label="Ruta en disco" value={media.path} />
				<DetailRow label="Formato" value={media.extension.replace(/^\./, "").toUpperCase()} />
				<DetailRow label="Peso" value={formatBytes(media.size)} />
				<DetailRow label="Páginas" value={String(media.pages)} />
				<DetailRow label="Estado" value={media.status} />
				<DetailRow label="Añadido" value={new Date(media.createdAt).toLocaleString("es-ES")} />
				<DetailRow label="Hash" value={media.hash} />
				<DetailRow label="Hash KOReader" value={media.koreaderHash} />
				<DetailRow label="Editorial" value={media.metadata?.publisher} />
				<DetailRow label="Autores" value={media.metadata?.writers} />
				<DetailRow label="Géneros" value={media.metadata?.genre} />
			</Box>
		</Box>
	);
}
