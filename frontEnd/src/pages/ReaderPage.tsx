import { Box, CircularProgress, IconButton, Slider, Tooltip, Typography } from "@mui/material";
import CloseIcon from "@mui/icons-material/Close";
import SettingsIcon from "@mui/icons-material/Settings";
import FullscreenIcon from "@mui/icons-material/Fullscreen";
import FullscreenExitIcon from "@mui/icons-material/FullscreenExit";
import ChevronLeftIcon from "@mui/icons-material/ChevronLeft";
import ChevronRightIcon from "@mui/icons-material/ChevronRight";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import ReaderSettingsPanel from "../reader/ReaderSettingsPanel";
import {
	DEFAULT_READER_SETTINGS,
	loadReaderSettings,
	saveReaderSettings,
	settingsFromLibrary,
	type ReaderSettings,
} from "../reader/readerSettings";
import { librariesApi, mediaApi, seriesApi, type MediaItem } from "../api/endpoints";
import { mediaTitle } from "../catalog/mediaHelpers";
import { DS } from "../theme";

const PRELOAD_AHEAD = 3;
const UI_HIDE_MS = 2500;
const PROGRESS_DEBOUNCE_MS = 1200;
/** Proporción de partida mientras no se sabe la real: la de un tomo de manga. */
const FALLBACK_RATIO = 2 / 3;
const SWIPE_THRESHOLD = 60;

interface Loaded {
	key: string;
	media?: MediaItem;
	error?: string;
}

export default function ReaderPage() {
	const { mediaId = "" } = useParams();
	const navigate = useNavigate();

	// El tomo cargado se guarda junto al id que lo pidió: así el estado de carga se
	// deriva de la ruta actual en vez de reiniciarse dentro de un efecto.
	const [loaded, setLoaded] = useState<Loaded | null>(null);
	const [page, setPage] = useState(1);
	const [settings, setSettings] = useState<ReaderSettings>(() => loadReaderSettings() ?? DEFAULT_READER_SETTINGS);
	const [uiVisible, setUiVisible] = useState(true);
	const [panelOpen, setPanelOpen] = useState(false);
	const [isFullscreen, setIsFullscreen] = useState(false);
	const [sliderValue, setSliderValue] = useState(1);

	const fresh = loaded?.key === mediaId ? loaded : null;
	const loading = fresh === null;
	const media = fresh?.media ?? null;
	const error = fresh?.error ?? null;

	const totalPages = media?.pages ?? 0;
	const step = settings.mode === "Double" ? 2 : 1;
	const rtl = settings.direction === "RightToLeft";

	// Proporción real de cada página ya cargada. Sin endpoint que exponga `MediaPages`,
	// se aprende de las imágenes que ya llegaron y se reserva el hueco con la última
	// conocida: es lo que evita el salto de maquetación al pasar de página. Va en estado
	// y no en un ref porque el render la lee para reservar el hueco.
	const [ratios, setRatios] = useState<Record<number, number>>({});
	const [lastRatio, setLastRatio] = useState(FALLBACK_RATIO);

	const hideTimer = useRef<number | null>(null);
	const progressTimer = useRef<number | null>(null);
	const pageRef = useRef(1);
	const touchStart = useRef<{ x: number; y: number } | null>(null);
	const containerRef = useRef<HTMLDivElement | null>(null);

	useEffect(() => {
		pageRef.current = page;
	}, [page]);

	// --- Carga del tomo y de los valores por defecto de su biblioteca ---------
	useEffect(() => {
		let mounted = true;

		const init = async () => {
			const item = await mediaApi.get(mediaId);
			if (!mounted) return;

			const start = item.currentPage && item.currentPage > 0 ? Math.min(item.currentPage, item.pages) : 1;
			setPage(start);
			setSliderValue(start);
			setLoaded({ key: mediaId, media: item });

			// Los ajustes guardados mandan; los de la biblioteca solo siembran la primera vez.
			if (loadReaderSettings() === null && item.seriesId) {
				const series = await seriesApi.get(item.seriesId).catch(() => null);
				const library = series ? await librariesApi.get(series.libraryId).catch(() => null) : null;
				if (mounted && library?.config) {
					setSettings(settingsFromLibrary(library.config.defaultReadingMode, library.config.defaultReadingDir));
				}
			}
		};

		void init().catch((err: unknown) => {
			if (mounted) {
				setLoaded({ key: mediaId, error: err instanceof Error ? err.message : "No se pudo abrir el tomo." });
			}
		});

		return () => {
			mounted = false;
		};
	}, [mediaId]);

	// --- Persistencia del progreso -------------------------------------------
	const pushProgress = useCallback(
		(value: number) => {
			if (!mediaId || totalPages <= 0) return;
			const percentage = Math.min(1, value / totalPages);
			void mediaApi
				.updateProgress(mediaId, {
					page: value,
					percentage,
					isCompleted: value >= totalPages,
				})
				.catch(() => {
					// El progreso no es crítico: si el nodo no responde, la lectura sigue.
				});
		},
		[mediaId, totalPages]
	);

	useEffect(() => {
		if (loading || totalPages <= 0) return;

		if (progressTimer.current !== null) {
			window.clearTimeout(progressTimer.current);
		}
		progressTimer.current = window.setTimeout(() => pushProgress(page), PROGRESS_DEBOUNCE_MS);

		return () => {
			if (progressTimer.current !== null) {
				window.clearTimeout(progressTimer.current);
			}
		};
	}, [page, loading, totalPages, pushProgress]);

	// Al salir del lector se manda la última página sin esperar al retardo.
	useEffect(() => {
		return () => {
			if (progressTimer.current !== null) {
				window.clearTimeout(progressTimer.current);
			}
			pushProgress(pageRef.current);
		};
	}, [pushProgress]);

	// --- Precarga -------------------------------------------------------------
	useEffect(() => {
		if (!media || totalPages <= 0) return;
		for (let offset = 1; offset <= PRELOAD_AHEAD; offset += 1) {
			const target = page + offset;
			if (target > totalPages) break;
			const img = new Image();
			img.src = mediaApi.pageUrl(media.id, target);
		}
	}, [media, page, totalPages]);

	// --- Interfaz que se esconde sola ----------------------------------------
	const wakeUi = useCallback(() => {
		setUiVisible(true);
		if (hideTimer.current !== null) {
			window.clearTimeout(hideTimer.current);
		}
		hideTimer.current = window.setTimeout(() => setUiVisible(false), UI_HIDE_MS);
	}, []);

	useEffect(() => {
		hideTimer.current = window.setTimeout(() => setUiVisible(false), UI_HIDE_MS);
		return () => {
			if (hideTimer.current !== null) {
				window.clearTimeout(hideTimer.current);
			}
		};
	}, []);

	// --- Navegación -----------------------------------------------------------
	const goTo = useCallback(
		(next: number) => {
			if (totalPages <= 0) return;
			const clamped = Math.max(1, Math.min(totalPages, next));
			setPage(clamped);
			setSliderValue(clamped);
			wakeUi();
		},
		[totalPages, wakeUi]
	);

	const goNext = useCallback(() => goTo(pageRef.current + step), [goTo, step]);
	const goPrev = useCallback(() => goTo(pageRef.current - step), [goTo, step]);

	const exit = useCallback(() => {
		void navigate(media ? `/media/${media.id}` : "/");
	}, [media, navigate]);

	const toggleFullscreen = useCallback(() => {
		if (document.fullscreenElement) {
			void document.exitFullscreen().catch(() => undefined);
		} else {
			void containerRef.current?.requestFullscreen().catch(() => undefined);
		}
	}, []);

	useEffect(() => {
		const onChange = () => setIsFullscreen(Boolean(document.fullscreenElement));
		document.addEventListener("fullscreenchange", onChange);
		return () => document.removeEventListener("fullscreenchange", onChange);
	}, []);

	useEffect(() => {
		const onKey = (e: KeyboardEvent) => {
			// En derecha-a-izquierda las flechas se invierten: la izquierda avanza.
			switch (e.key) {
				case "ArrowRight":
					if (rtl) goPrev();
					else goNext();
					break;
				case "ArrowLeft":
					if (rtl) goNext();
					else goPrev();
					break;
				case " ":
				case "PageDown":
					e.preventDefault();
					goNext();
					break;
				case "PageUp":
					e.preventDefault();
					goPrev();
					break;
				case "f":
				case "F":
					toggleFullscreen();
					break;
				case "Escape":
					if (!document.fullscreenElement) exit();
					break;
				default:
					break;
			}
		};

		window.addEventListener("keydown", onKey);
		return () => window.removeEventListener("keydown", onKey);
	}, [goNext, goPrev, rtl, toggleFullscreen, exit]);

	// --- Gestos táctiles ------------------------------------------------------
	const onTouchStart = (e: React.TouchEvent) => {
		const t = e.touches[0];
		touchStart.current = { x: t.clientX, y: t.clientY };
	};

	const onTouchEnd = (e: React.TouchEvent) => {
		const start = touchStart.current;
		touchStart.current = null;
		if (!start || settings.mode === "ContinuousVertical") {
			wakeUi();
			return;
		}
		const t = e.changedTouches[0];
		const dx = t.clientX - start.x;
		const dy = t.clientY - start.y;
		if (Math.abs(dx) < SWIPE_THRESHOLD || Math.abs(dx) < Math.abs(dy)) {
			wakeUi();
			return;
		}
		const forward = dx < 0 ? !rtl : rtl;
		if (forward) goNext();
		else goPrev();
	};

	// --- Render de páginas ----------------------------------------------------
	const rememberRatio = (pageNumber: number, img: HTMLImageElement) => {
		if (img.naturalWidth <= 0 || img.naturalHeight <= 0) return;
		const ratio = img.naturalWidth / img.naturalHeight;
		setRatios((prev) => (prev[pageNumber] === ratio ? prev : { ...prev, [pageNumber]: ratio }));
		setLastRatio(ratio);
	};

	const imageSx = useMemo(() => {
		if (settings.fit === "width") {
			return { width: "100%", height: "auto", maxWidth: "100%" };
		}
		if (settings.fit === "original") {
			return { width: "auto", height: "auto", maxWidth: "none" };
		}
		return { height: "100%", width: "auto", maxHeight: "100%", objectFit: "contain" as const };
	}, [settings.fit]);

	const renderPage = (pageNumber: number, key: string) => {
		if (!media || pageNumber > totalPages || pageNumber < 1) return null;
		const ratio = ratios[pageNumber] ?? lastRatio;
		return (
			<Box
				key={key}
				sx={{
					// El hueco se reserva con la proporción conocida antes de que llegue la imagen.
					aspectRatio: settings.fit === "original" ? undefined : String(ratio),
					display: "flex",
					alignItems: "center",
					justifyContent: "center",
					maxHeight: settings.mode === "ContinuousVertical" ? "none" : "100%",
					flexShrink: 0,
				}}
			>
				<Box
					component="img"
					src={mediaApi.pageUrl(media.id, pageNumber)}
					alt={`Página ${String(pageNumber)}`}
					loading={settings.mode === "ContinuousVertical" ? "lazy" : "eager"}
					onLoad={(e: React.SyntheticEvent<HTMLImageElement>) => rememberRatio(pageNumber, e.currentTarget)}
					sx={{ display: "block", ...imageSx }}
				/>
			</Box>
		);
	};

	// En tira continua la página actual es la que domina la ventana.
	useEffect(() => {
		if (settings.mode !== "ContinuousVertical" || !media) return;
		const root = containerRef.current;
		if (!root) return;

		const observer = new IntersectionObserver(
			(entries) => {
				for (const entry of entries) {
					if (entry.isIntersecting && entry.intersectionRatio > 0.5) {
						const value = Number((entry.target as HTMLElement).dataset.page);
						if (!Number.isNaN(value)) {
							setPage(value);
							setSliderValue(value);
						}
					}
				}
			},
			{ threshold: [0.5] }
		);

		for (const node of root.querySelectorAll("[data-page]")) {
			observer.observe(node);
		}
		return () => observer.disconnect();
	}, [settings.mode, media, totalPages]);

	const updateSettings = (next: ReaderSettings) => {
		setSettings(next);
		saveReaderSettings(next);
		wakeUi();
	};

	if (loading) {
		return (
			<Box sx={{ height: "100vh", display: "flex", alignItems: "center", justifyContent: "center", backgroundColor: "#000000" }}>
				<CircularProgress size={32} thickness={5} />
			</Box>
		);
	}

	if (error || !media) {
		return (
			<Box
				sx={{
					height: "100vh",
					display: "flex",
					flexDirection: "column",
					alignItems: "center",
					justifyContent: "center",
					gap: 2,
					backgroundColor: "#000000",
				}}
			>
				<Typography sx={{ fontFamily: "'Rajdhani', sans-serif", fontWeight: 700, letterSpacing: "1px", color: DS.redGlow }}>
					{error ?? "TOMO NO DISPONIBLE"}
				</Typography>
				<IconButton onClick={exit} sx={{ color: DS.muted }}>
					<CloseIcon />
				</IconButton>
			</Box>
		);
	}

	let canvas: React.ReactNode;
	if (settings.mode === "ContinuousVertical") {
		canvas = (
			<Box sx={{ display: "flex", flexDirection: "column", alignItems: "center", gap: 0.5, width: "100%" }}>
				{Array.from({ length: totalPages }, (_, i) => i + 1).map((n) => (
					<Box key={`strip-${String(n)}`} data-page={n} sx={{ width: "100%", maxWidth: 1000 }}>
						{renderPage(n, `img-${String(n)}`)}
					</Box>
				))}
			</Box>
		);
	} else if (settings.mode === "Double") {
		canvas = (
			<Box
				sx={{
					display: "flex",
					flexDirection: rtl ? "row-reverse" : "row",
					alignItems: "center",
					justifyContent: "center",
					gap: 0.5,
					height: "100%",
				}}
			>
				{renderPage(page, `d-${String(page)}`)}
				{renderPage(page + 1, `d-${String(page + 1)}`)}
			</Box>
		);
	} else {
		canvas = (
			<Box sx={{ display: "flex", alignItems: "center", justifyContent: "center", height: "100%" }}>
				{renderPage(page, `p-${String(page)}`)}
			</Box>
		);
	}

	return (
		<Box
			ref={containerRef}
			onMouseMove={wakeUi}
			onTouchStart={onTouchStart}
			onTouchEnd={onTouchEnd}
			sx={{
				position: "fixed",
				inset: 0,
				backgroundColor: settings.background,
				overflow: settings.mode === "ContinuousVertical" || settings.fit === "original" ? "auto" : "hidden",
				cursor: uiVisible ? "default" : "none",
			}}
		>
			{canvas}

			{/* Mitades invisibles: hacen del lienzo el propio control de paso de página. */}
			{settings.mode !== "ContinuousVertical" && (
				<>
					<Box
						onClick={() => (rtl ? goNext() : goPrev())}
						sx={{ position: "absolute", top: 0, bottom: 0, left: 0, width: "25%", cursor: "w-resize" }}
					/>
					<Box
						onClick={() => (rtl ? goPrev() : goNext())}
						sx={{ position: "absolute", top: 0, bottom: 0, right: 0, width: "25%", cursor: "e-resize" }}
					/>
				</>
			)}

			{/* Barra superior */}
			<Box
				sx={{
					position: "absolute",
					top: 0,
					left: 0,
					right: 0,
					display: "flex",
					alignItems: "center",
					gap: 1.5,
					px: 2,
					py: 1.25,
					background: "linear-gradient(180deg, rgba(5, 5, 8, 0.95) 0%, transparent 100%)",
					borderBottom: `1px solid ${DS.border}`,
					opacity: uiVisible ? 1 : 0,
					transform: uiVisible ? "translateY(0)" : "translateY(-100%)",
					transition: "opacity 0.25s ease, transform 0.25s ease",
					pointerEvents: uiVisible ? "auto" : "none",
					zIndex: 10,
				}}
			>
				<Tooltip title="Salir del lector">
					<IconButton onClick={exit} sx={{ color: DS.muted, "&:hover": { color: DS.redGlow } }}>
						<CloseIcon fontSize="small" />
					</IconButton>
				</Tooltip>

				<Typography
					sx={{
						flexGrow: 1,
						minWidth: 0,
						fontFamily: "'Rajdhani', sans-serif",
						fontSize: "15px",
						fontWeight: 700,
						letterSpacing: "1px",
						textTransform: "uppercase",
						color: "#FFFFFF",
						overflow: "hidden",
						textOverflow: "ellipsis",
						whiteSpace: "nowrap",
					}}
				>
					{mediaTitle(media)}
				</Typography>

				<Box component="span" className="ds-pill-mono">
					{String(page).padStart(3, "0")} / {String(totalPages).padStart(3, "0")}
				</Box>

				<Tooltip title={isFullscreen ? "Salir de pantalla completa" : "Pantalla completa"}>
					<IconButton onClick={toggleFullscreen} sx={{ color: DS.muted, "&:hover": { color: "#FFFFFF" } }}>
						{isFullscreen ? <FullscreenExitIcon fontSize="small" /> : <FullscreenIcon fontSize="small" />}
					</IconButton>
				</Tooltip>

				<Tooltip title="Ajustes del lector">
					<IconButton onClick={() => setPanelOpen(true)} sx={{ color: DS.muted, "&:hover": { color: "#FFFFFF" } }}>
						<SettingsIcon fontSize="small" />
					</IconButton>
				</Tooltip>
			</Box>

			{/* Barra inferior: salto directo con miniatura de la página apuntada */}
			<Box
				sx={{
					position: "absolute",
					bottom: 0,
					left: 0,
					right: 0,
					px: 3,
					py: 1.5,
					background: "linear-gradient(0deg, rgba(5, 5, 8, 0.95) 0%, transparent 100%)",
					borderTop: `1px solid ${DS.border}`,
					opacity: uiVisible ? 1 : 0,
					transform: uiVisible ? "translateY(0)" : "translateY(100%)",
					transition: "opacity 0.25s ease, transform 0.25s ease",
					pointerEvents: uiVisible ? "auto" : "none",
					zIndex: 10,
				}}
			>
				<Box sx={{ display: "flex", alignItems: "center", gap: 2 }}>
					<IconButton
						onClick={() => (rtl ? goNext() : goPrev())}
						disabled={rtl ? page >= totalPages : page <= 1}
						sx={{ color: DS.muted }}
					>
						<ChevronLeftIcon />
					</IconButton>

					<Box sx={{ flexGrow: 1, position: "relative" }}>
						{sliderValue !== page && (
							<Box
								sx={{
									position: "absolute",
									bottom: 28,
									left: `${String(((sliderValue - 1) / Math.max(1, totalPages - 1)) * 100)}%`,
									transform: "translateX(-50%)",
									width: 80,
									border: `1px solid ${DS.red}`,
									backgroundColor: DS.bgSunken,
									pointerEvents: "none",
								}}
							>
								<Box
									component="img"
									src={mediaApi.pageUrl(media.id, sliderValue)}
									alt=""
									sx={{ width: "100%", display: "block", aspectRatio: String(lastRatio), objectFit: "cover" }}
								/>
							</Box>
						)}

						<Slider
							value={sliderValue}
							min={1}
							max={Math.max(1, totalPages)}
							onChange={(_, value) => setSliderValue(value)}
							onChangeCommitted={(_, value) => goTo(value)}
							sx={{
								color: DS.red,
								direction: rtl ? "rtl" : "ltr",
								"& .MuiSlider-thumb": { borderRadius: 0, width: 10, height: 18 },
								"& .MuiSlider-rail": { backgroundColor: DS.border },
							}}
						/>
					</Box>

					<IconButton
						onClick={() => (rtl ? goPrev() : goNext())}
						disabled={rtl ? page <= 1 : page >= totalPages}
						sx={{ color: DS.muted }}
					>
						<ChevronRightIcon />
					</IconButton>
				</Box>
			</Box>

			<ReaderSettingsPanel
				open={panelOpen}
				onClose={() => setPanelOpen(false)}
				settings={settings}
				onChange={updateSettings}
			/>
		</Box>
	);
}
