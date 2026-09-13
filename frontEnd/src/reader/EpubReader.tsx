import {
	Box,
	Button,
	CircularProgress,
	Divider,
	Drawer,
	IconButton,
	List,
	ListItemButton,
	ListItemText,
	Stack,
	Tooltip,
	Typography,
} from "@mui/material";
import CloseIcon from "@mui/icons-material/Close";
import FormatListBulletedIcon from "@mui/icons-material/FormatListBulleted";
import FormatSizeIcon from "@mui/icons-material/FormatSize";
import FullscreenIcon from "@mui/icons-material/Fullscreen";
import FullscreenExitIcon from "@mui/icons-material/FullscreenExit";
import ChevronLeftIcon from "@mui/icons-material/ChevronLeft";
import ChevronRightIcon from "@mui/icons-material/ChevronRight";
import { useCallback, useEffect, useRef, useState } from "react";
import ePub, { type Book, type NavItem, type Rendition } from "epubjs";
import { mediaApi, type MediaItem } from "../api/endpoints";
import { downloadBinary } from "../api/client";
import { formatBytes, mediaTitle } from "../catalog/mediaHelpers";
import { DS } from "../theme";

interface Props {
	media: MediaItem;
	onExit: () => void;
}

const EPUB_THEMES = {
	ironBlood: {
		body: {
			background: "#050508 !important",
			color: "#F0F2F6 !important",
			"font-family": "'Inter', sans-serif !important",
			"line-height": "1.75 !important",
			padding: "0 24px !important",
		},
		p: { color: "#F0F2F6 !important" },
		"h1, h2, h3, h4": { color: "#FF2E2E !important", "font-family": "'Rajdhani', sans-serif !important" },
		a: { color: "#FF2E2E !important" },
	},
	pureBlack: {
		body: {
			background: "#000000 !important",
			color: "#D1D5DB !important",
			"font-family": "'Inter', sans-serif !important",
			"line-height": "1.75 !important",
			padding: "0 24px !important",
		},
		p: { color: "#D1D5DB !important" },
		"h1, h2, h3, h4": { color: "#FFFFFF !important" },
		a: { color: "#9CA3AF !important" },
	},
	sepia: {
		body: {
			background: "#1C1814 !important",
			color: "#E2D9CE !important",
			"font-family": "'Inter', sans-serif !important",
			"line-height": "1.75 !important",
			padding: "0 24px !important",
		},
		p: { color: "#E2D9CE !important" },
		"h1, h2, h3, h4": { color: "#E8A87C !important" },
		a: { color: "#E8A87C !important" },
	},
};

export default function EpubReader({ media, onExit }: Readonly<Props>) {
	const viewerRef = useRef<HTMLDivElement | null>(null);
	const bookRef = useRef<Book | null>(null);
	const renditionRef = useRef<Rendition | null>(null);

	const [loading, setLoading] = useState(true);
	const [statusText, setStatusText] = useState("Conectando con el almacén...");
	const [error, setError] = useState<string | null>(null);
	const [uiVisible, setUiVisible] = useState(true);
	const [isFullscreen, setIsFullscreen] = useState(false);
	const [tocOpen, setTocOpen] = useState(false);
	const [toc, setToc] = useState<NavItem[]>([]);
	const [fontSize, setFontSize] = useState<number>(100);
	const [currentTheme, setCurrentTheme] = useState<"ironBlood" | "pureBlack" | "sepia">("ironBlood");
	const [currentProgress, setCurrentProgress] = useState<number>(media.currentPage ?? 1);
	const [totalLocations, setTotalLocations] = useState<number>(media.pages > 0 ? media.pages : 1);
	const [settingsOpen, setSettingsOpen] = useState(false);

	const hideTimer = useRef<number | null>(null);

	const wakeUi = useCallback(() => {
		setUiVisible(true);
		if (hideTimer.current) window.clearTimeout(hideTimer.current);
		hideTimer.current = window.setTimeout(() => {
			setUiVisible(false);
			setSettingsOpen(false);
		}, 3000);
	}, []);

	// Inicializar y descargar ePub
	useEffect(() => {
		let isMounted = true;
		const container = viewerRef.current;
		if (!container) return;

		setLoading(true);
		setError(null);
		setStatusText("Descargando archivo...");

		const loadBook = async () => {
			try {
				const buffer = await downloadBinary(mediaApi.fileUrl(media.id), (loaded, total) => {
					if (!isMounted) return;
					const pct = total > 0 ? Math.round((loaded / total) * 100) : 0;
					const loadedStr = formatBytes(loaded);
					const totalStr = total > 0 ? formatBytes(total) : "";
					setStatusText(`Descargando libro: ${loadedStr}${totalStr ? ` de ${totalStr}` : ""} (${String(pct)}%)`);
				});

				if (!isMounted) return;
				setStatusText("Desempaquetando libro y maquetando...");

				const book = ePub(buffer);
				bookRef.current = book;

				const rendition = book.renderTo(container, {
					width: "100%",
					height: "100%",
					flow: "paginated",
					spread: "none",
				});
				renditionRef.current = rendition;

				rendition.themes.register("ironBlood", EPUB_THEMES.ironBlood);
				rendition.themes.register("pureBlack", EPUB_THEMES.pureBlack);
				rendition.themes.register("sepia", EPUB_THEMES.sepia);
				rendition.themes.select("ironBlood");

				await rendition.display();

				if (!isMounted) return;
				setLoading(false);
				wakeUi();

				// Generar tabla de contenidos
				void book.loaded.navigation
					.then((nav) => {
						if (isMounted && nav?.toc) {
							setToc(nav.toc);
						}
					})
					.catch(() => undefined);

				// Generar localizaciones para cálculo de páginas
				void book.locations.generate(1024).then(() => {
					if (!isMounted) return;
					const total = media.pages > 0 ? media.pages : 100;
					setTotalLocations(total);
				}).catch(() => undefined);

				// Evento de cambio de página/ubicación
				rendition.on("relocated", (loc: unknown) => {
					const location = loc as { start?: { displayed?: { page: number; total: number }; percentage?: number } } | null;
					if (location?.start) {
						if (location.start.displayed && location.start.displayed.page > 0) {
							const cur = location.start.displayed.page;
							const tot = location.start.displayed.total;
							setCurrentProgress(cur);
							if (tot > 0) setTotalLocations(tot);
							void mediaApi.updateProgress(media.id, { page: cur, isCompleted: cur >= tot }).catch(() => undefined);
						} else if (typeof location.start.percentage === "number") {
							const pct = Math.max(1, Math.round(location.start.percentage * 100));
							setCurrentProgress(pct);
							setTotalLocations(100);
							void mediaApi.updateProgress(media.id, { page: pct, percentage: pct, isCompleted: pct >= 99 }).catch(() => undefined);
						}
					}
				});

				rendition.on("click", () => {
					wakeUi();
				});
			} catch (err: unknown) {
				if (!isMounted) return;
				setError(err instanceof Error ? err.message : "Error al abrir el archivo EPUB.");
				setLoading(false);
			}
		};

		void loadBook();

		return () => {
			isMounted = false;
			try {
				renditionRef.current?.destroy();
				bookRef.current?.destroy();
			} catch {
				// Silencio al desmontar
			}
		};
	}, [media.id, media.pages, wakeUi]);

	const goNext = useCallback(() => {
		if (renditionRef.current) {
			void renditionRef.current.next();
			wakeUi();
		}
	}, [wakeUi]);

	const goPrev = useCallback(() => {
		if (renditionRef.current) {
			void renditionRef.current.prev();
			wakeUi();
		}
	}, [wakeUi]);

	// Teclas de dirección
	useEffect(() => {
		const handleKeyDown = (e: KeyboardEvent) => {
			if (e.key === "ArrowRight" || e.key === " " || e.key === "PageDown") {
				e.preventDefault();
				goNext();
			} else if (e.key === "ArrowLeft" || e.key === "PageUp") {
				e.preventDefault();
				goPrev();
			} else if (e.key === "Escape") {
				onExit();
			}
		};

		window.addEventListener("keydown", handleKeyDown);
		return () => window.removeEventListener("keydown", handleKeyDown);
	}, [goNext, goPrev, onExit]);

	const changeFontSize = (delta: number) => {
		const next = Math.max(70, Math.min(180, fontSize + delta));
		setFontSize(next);
		renditionRef.current?.themes.fontSize(`${String(next)}%`);
		wakeUi();
	};

	const selectTheme = (theme: "ironBlood" | "pureBlack" | "sepia") => {
		setCurrentTheme(theme);
		renditionRef.current?.themes.select(theme);
		wakeUi();
	};

	const navigateToChapter = (href: string) => {
		void renditionRef.current?.display(href);
		setTocOpen(false);
		wakeUi();
	};

	const toggleFullscreen = () => {
		if (!document.fullscreenElement) {
			void document.documentElement.requestFullscreen();
			setIsFullscreen(true);
		} else {
			void document.exitFullscreen();
			setIsFullscreen(false);
		}
	};

	const getBgColor = () => {
		if (currentTheme === "pureBlack") return "#000000";
		if (currentTheme === "sepia") return "#1C1814";
		return "#050508";
	};

	return (
		<Box
			onMouseMove={wakeUi}
			onClick={wakeUi}
			sx={{
				position: "fixed",
				inset: 0,
				backgroundColor: getBgColor(),
				zIndex: 9999,
				display: "flex",
				flexDirection: "column",
				overflow: "hidden",
				userSelect: "none",
			}}
		>
			{/* Barra Superior */}
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
					zIndex: 20,
				}}
			>
				<Tooltip title="Salir del lector">
					<IconButton onClick={onExit} sx={{ color: DS.muted, "&:hover": { color: DS.redGlow } }}>
						<CloseIcon fontSize="small" />
					</IconButton>
				</Tooltip>

				<Tooltip title="Capítulos (TOC)">
					<IconButton onClick={() => setTocOpen(true)} sx={{ color: DS.muted, "&:hover": { color: "#FFFFFF" } }}>
						<FormatListBulletedIcon fontSize="small" />
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
					PAG {String(currentProgress)} / {String(totalLocations)}
				</Box>

				<Tooltip title="Ajustes de texto">
					<IconButton onClick={() => setSettingsOpen((prev) => !prev)} sx={{ color: DS.muted, "&:hover": { color: "#FFFFFF" } }}>
						<FormatSizeIcon fontSize="small" />
					</IconButton>
				</Tooltip>

				<Tooltip title={isFullscreen ? "Salir de pantalla completa" : "Pantalla completa"}>
					<IconButton onClick={toggleFullscreen} sx={{ color: DS.muted, "&:hover": { color: "#FFFFFF" } }}>
						{isFullscreen ? <FullscreenExitIcon fontSize="small" /> : <FullscreenIcon fontSize="small" />}
					</IconButton>
				</Tooltip>
			</Box>

			{/* Panel flotante de Ajustes */}
			{settingsOpen && (
				<Box
					onClick={(e) => e.stopPropagation()}
					sx={{
						position: "absolute",
						top: 60,
						right: 16,
						backgroundColor: DS.bgCard,
						border: `1px solid ${DS.border}`,
						p: 2,
						zIndex: 30,
						width: 260,
						boxShadow: "0 8px 32px rgba(0,0,0,0.8)",
					}}
				>
					<Typography sx={{ fontFamily: "'Rajdhani', sans-serif", fontSize: "12px", fontWeight: 700, color: DS.redLight, mb: 1 }}>
						TAMAÑO DE FUENTE
					</Typography>
					<Stack direction="row" spacing={1} sx={{ alignItems: "center", mb: 2 }}>
						<Button size="small" variant="outlined" onClick={() => changeFontSize(-10)} sx={{ minWidth: 36 }}>
							A-
						</Button>
						<Typography sx={{ flex: 1, textAlign: "center", fontFamily: "'JetBrains Mono', monospace", fontSize: "13px" }}>
							{fontSize}%
						</Typography>
						<Button size="small" variant="outlined" onClick={() => changeFontSize(10)} sx={{ minWidth: 36 }}>
							A+
						</Button>
					</Stack>

					<Typography sx={{ fontFamily: "'Rajdhani', sans-serif", fontSize: "12px", fontWeight: 700, color: DS.redLight, mb: 1 }}>
						TEMA DE LECTURA
					</Typography>
					<Stack direction="row" spacing={1}>
						<Button
							size="small"
							variant={currentTheme === "ironBlood" ? "contained" : "outlined"}
							onClick={() => selectTheme("ironBlood")}
							sx={{ fontSize: "11px", flex: 1 }}
						>
							Core
						</Button>
						<Button
							size="small"
							variant={currentTheme === "pureBlack" ? "contained" : "outlined"}
							onClick={() => selectTheme("pureBlack")}
							sx={{ fontSize: "11px", flex: 1 }}
						>
							OLED
						</Button>
						<Button
							size="small"
							variant={currentTheme === "sepia" ? "contained" : "outlined"}
							onClick={() => selectTheme("sepia")}
							sx={{ fontSize: "11px", flex: 1 }}
						>
							Sepia
						</Button>
					</Stack>
				</Box>
			)}

			{/* Contenedor del Libro */}
			<Box sx={{ flex: 1, position: "relative", width: "100%", height: "100%", overflow: "hidden" }}>
				{loading && (
					<Box sx={{ position: "absolute", inset: 0, display: "flex", flexDirection: "column", alignItems: "center", justifyContent: "center", gap: 2, zIndex: 10, backgroundColor: getBgColor() }}>
						<CircularProgress size={42} thickness={4} sx={{ color: DS.red }} />
						<Typography sx={{ fontFamily: "'JetBrains Mono', monospace", fontSize: "12px", color: DS.muted, textAlign: "center" }}>
							{statusText}
						</Typography>
					</Box>
				)}

				{error && (
					<Box sx={{ position: "absolute", inset: 0, display: "flex", flexDirection: "column", alignItems: "center", justifyContent: "center", gap: 2, zIndex: 10, backgroundColor: getBgColor() }}>
						<Typography sx={{ fontFamily: "'Rajdhani', sans-serif", color: DS.redGlow, fontSize: "16px", fontWeight: 700 }}>
							{error}
						</Typography>
						<Button variant="outlined" onClick={onExit} sx={{ borderColor: DS.borderRed, color: DS.platinum }}>
							SALIR
						</Button>
					</Box>
				)}

				{/* Div donde ePub.js monta el iframe */}
				<Box
					ref={viewerRef}
					sx={{
						width: "100%",
						height: "100%",
						maxWidth: "960px",
						mx: "auto",
					}}
				/>

				{/* Zonas de Clic para avanzar/retroceder página */}
				<Box
					onClick={(e) => {
						e.stopPropagation();
						goPrev();
					}}
					sx={{
						position: "absolute",
						top: 60,
						bottom: 60,
						left: 0,
						width: { xs: "15%", sm: "20%" },
						cursor: "w-resize",
					}}
				/>
				<Box
					onClick={(e) => {
						e.stopPropagation();
						goNext();
					}}
					sx={{
						position: "absolute",
						top: 60,
						bottom: 60,
						right: 0,
						width: { xs: "15%", sm: "20%" },
						cursor: "e-resize",
					}}
				/>
			</Box>

			{/* Barra Inferior */}
			<Box
				sx={{
					position: "absolute",
					bottom: 0,
					left: 0,
					right: 0,
					display: "flex",
					alignItems: "center",
					justifyContent: "space-between",
					px: 3,
					py: 1,
					background: "linear-gradient(0deg, rgba(5, 5, 8, 0.95) 0%, transparent 100%)",
					opacity: uiVisible ? 1 : 0,
					transform: uiVisible ? "translateY(0)" : "translateY(100%)",
					transition: "opacity 0.25s ease, transform 0.25s ease",
					pointerEvents: uiVisible ? "auto" : "none",
					zIndex: 20,
				}}
			>
				<IconButton onClick={goPrev} sx={{ color: DS.muted, "&:hover": { color: "#FFFFFF" } }}>
					<ChevronLeftIcon />
				</IconButton>

				<Typography sx={{ fontFamily: "'JetBrains Mono', monospace", fontSize: "12px", color: DS.muted }}>
					{media.name}
				</Typography>

				<IconButton onClick={goNext} sx={{ color: DS.muted, "&:hover": { color: "#FFFFFF" } }}>
					<ChevronRightIcon />
				</IconButton>
			</Box>

			{/* Drawer lateral de Tabla de Contenidos */}
			<Drawer
				anchor="left"
				open={tocOpen}
				onClose={() => setTocOpen(false)}
				slotProps={{
					paper: {
						sx: {
							width: 320,
							backgroundColor: DS.bgCard,
							borderRight: `1px solid ${DS.border}`,
							p: 2,
						},
					},
				}}
			>
				<Stack direction="row" sx={{ alignItems: "center", justifyContent: "space-between", mb: 2 }}>
					<Typography sx={{ fontFamily: "'Rajdhani', sans-serif", fontSize: "16px", fontWeight: 700, color: DS.redLight }}>
						{"// ÍNDICE DE CAPÍTULOS"}
					</Typography>
					<IconButton onClick={() => setTocOpen(false)} sx={{ color: DS.muted }}>
						<CloseIcon fontSize="small" />
					</IconButton>
				</Stack>
				<Divider sx={{ borderColor: DS.borderSoft, mb: 2 }} />
				<List sx={{ overflowY: "auto", flex: 1 }}>
					{toc.length === 0 ? (
						<Typography sx={{ fontFamily: "'Inter', sans-serif", fontSize: "13px", color: DS.muted, p: 2 }}>
							No hay índice disponible para este libro.
						</Typography>
					) : (
						toc.map((item) => (
							<ListItemButton
								key={item.id || item.href}
								onClick={() => navigateToChapter(item.href)}
								sx={{
									py: 1,
									borderRadius: 0,
									borderLeft: "2px solid transparent",
									"&:hover": { borderLeftColor: DS.red, backgroundColor: "rgba(255,46,46,0.06)" },
								}}
							>
								<ListItemText
									primary={item.label?.trim() || "Capítulo"}
									slotProps={{
										primary: {
											sx: {
												fontFamily: "'Inter', sans-serif",
												fontSize: "13px",
												color: DS.platinum,
											},
										},
									}}
								/>
							</ListItemButton>
						))
					)}
				</List>
			</Drawer>
		</Box>
	);
}
