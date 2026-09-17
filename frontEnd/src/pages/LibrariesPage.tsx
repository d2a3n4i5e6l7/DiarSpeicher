import { errorMessage } from "../api/errorMessage";
import {
	Alert,
	Box,
	Button,
	Card,
	CardContent,
	FormControlLabel,
	Switch,
	CircularProgress,
	Collapse,
	Dialog,
	DialogActions,
	DialogContent,
	DialogTitle,
	IconButton,
	LinearProgress,
	Stack,
	Tooltip,
	Typography,
} from "@mui/material";
import AddIcon from "@mui/icons-material/Add";
import EditOutlinedIcon from "@mui/icons-material/EditOutlined";
import DeleteOutlinedIcon from "@mui/icons-material/DeleteOutlined";
import ExpandMoreIcon from "@mui/icons-material/ExpandMore";
import LibraryBooksIcon from "@mui/icons-material/LibraryBooks";
import RefreshIcon from "@mui/icons-material/Refresh";
import SyncIcon from "@mui/icons-material/Sync";
import ChevronRightIcon from "@mui/icons-material/ChevronRight";
import FolderOpenIcon from "@mui/icons-material/FolderOpen";
import CloudUploadIcon from "@mui/icons-material/CloudUpload";
import FolderIcon from "@mui/icons-material/Folder";
import InfoOutlinedIcon from "@mui/icons-material/InfoOutlined";
import AutoStoriesIcon from "@mui/icons-material/AutoStories";
import CollectionsBookmarkIcon from "@mui/icons-material/CollectionsBookmark";
import React, { useCallback, useEffect, useRef, useState } from "react";
import { Link as RouterLink } from "react-router-dom";
import PageHeader from "../components/PageHeader";
import HudFrame from "../components/HudFrame";
import CatalogToolbar, { type SortDirection } from "../components/CatalogToolbar";
import { PANEL_GRID } from "../catalog/layout";
import { filterAndSortLibraries, LIBRARY_SORTS } from "../catalog/sorting";
import {
	filesystemApi,
	librariesApi,
	type FolderEntry,
	type LibraryItem,
	type ScanStatus,
} from "../api/endpoints";
import LibraryFormDialog from "../components/LibraryFormDialog";
import UploadPanel from "../components/UploadPanel";
import DeleteScopeNotice from "../components/DeleteScopeNotice";
import { scanLabel, formatDuration } from "../catalog/useScanProgress";

const SCAN_POLL_MS = 3000;

function formatDate(iso: string | null | undefined): string {
	if (!iso) return "NUNCA ESCANEADA";
	try {
		const d = new Date(iso);
		if (Number.isNaN(d.getTime())) return iso;
		return d.toLocaleString("es-ES", {
			day: "2-digit",
			month: "2-digit",
			year: "numeric",
			hour: "2-digit",
			minute: "2-digit",
		});
	} catch {
		return iso;
	}
}

function filterActiveScanningIds(scanningIds: string[], scans: ScanStatus[]): string[] {
	const activeSet = new Set(scans.map((s) => s.libraryId));
	return scanningIds.filter((id) => activeSet.has(id));
}

export default function LibrariesPage() {
	const [libraries, setLibraries] = useState<LibraryItem[]>([]);
	const [loading, setLoading] = useState(true);
	const [error, setError] = useState<string | null>(null);
	const [notice, setNotice] = useState<string | null>(null);

	useEffect(() => {
		if (!notice) return;

		const timer = window.setTimeout(() => {
			setNotice(null);
		}, 10000);

		return () => {
			window.clearTimeout(timer);
		};
	}, [notice]);
	const [scanningIds, setScanningIds] = useState<string[]>([]);

	const [query, setQuery] = useState("");
	const [sort, setSort] = useState("name");
	const [direction, setDirection] = useState<SortDirection>("asc");

	const [editing, setEditing] = useState<LibraryItem | null>(null);
	const [formOpen, setFormOpen] = useState(false);
	const [activeScans, setActiveScans] = useState<ScanStatus[]>([]);
	const [deleteFiles, setDeleteFiles] = useState(false);
	const [uploadTarget, setUploadTarget] = useState<{ library: LibraryItem; subpath: string } | null>(null);
	const [expanded, setExpanded] = useState<string | null>(null);
	const [saving, setSaving] = useState(false);
	const [pendingDelete, setPendingDelete] = useState<LibraryItem | null>(null);

	const pollRef = useRef<number | null>(null);

	const load = useCallback(async (silent = false) => {
		if (!silent) setLoading(true);
		try {
			const [list, scans] = await Promise.all([
				librariesApi.list(),
				librariesApi.activeScans().catch(() => [] as ScanStatus[]),
			]);
			setLibraries(list ?? []);
			setActiveScans(scans);
			setScanningIds((prev) => filterActiveScanningIds(prev, scans));
			if (!silent) setError(null);
		} catch (err: unknown) {
			setError(errorMessage(err, "Error cargando las bibliotecas."));
		} finally {
			if (!silent) setLoading(false);
		}
	}, []);

	useEffect(() => {
		let isMounted = true;
		const init = async () => {
			try {
				const [list, scans] = await Promise.all([
					librariesApi.list(),
					librariesApi.activeScans().catch(() => [] as ScanStatus[]),
				]);
				if (isMounted) {
					setLibraries(list ?? []);
					setActiveScans(scans);
					setScanningIds((prev) => filterActiveScanningIds(prev, scans));
					setError(null);
				}
			} catch (err: unknown) {
				if (isMounted) {
					setError(errorMessage(err, "Error cargando las bibliotecas."));
				}
			} finally {
				if (isMounted) {
					setLoading(false);
				}
			}
		};
		void init();
		return () => {
			isMounted = false;
		};
	}, []);

	useEffect(() => {
		const anyScanning = activeScans.length > 0 || scanningIds.length > 0;
		if (!anyScanning) {
			if (pollRef.current !== null) {
				window.clearInterval(pollRef.current);
				pollRef.current = null;
			}
			return;
		}

		if (pollRef.current !== null) return;

		pollRef.current = window.setInterval(() => {
			void load(true);
		}, SCAN_POLL_MS);

		return () => {
			if (pollRef.current !== null) {
				window.clearInterval(pollRef.current);
				pollRef.current = null;
			}
		};
	}, [activeScans.length, scanningIds.length, load]);

	const openForCreate = () => {
		setEditing(null);
		setFormOpen(true);
	};

	const openForEdit = (library: LibraryItem) => {
		setEditing(library);
		setFormOpen(true);
	};

	const handleDelete = async () => {
		if (!pendingDelete) return;
		setSaving(true);
		setError(null);
		try {
			await librariesApi.delete(pendingDelete.id, deleteFiles);
			setNotice(
				deleteFiles
					? `"${pendingDelete.name}" eliminada. Recuperable en Restaurar durante una hora.`
					: `Biblioteca "${pendingDelete.name}" eliminada del índice.`,
			);
			setDeleteFiles(false);
			setPendingDelete(null);
			await load(true);
		} catch (err: unknown) {
			setError(errorMessage(err, "Error eliminando la biblioteca."));
		} finally {
			setSaving(false);
		}
	};

	const handleScan = async (library: LibraryItem) => {
		setScanningIds((prev) => [...prev, library.id]);
		setError(null);
		try {
			await librariesApi.scan(library.id);
			setNotice(`Escaneo encolado para "${library.name}".`);
			await load(true);
		} catch (err: unknown) {
			setError(errorMessage(err, "No se pudo encolar el escaneo."));
			setScanningIds((prev) => prev.filter((id) => id !== library.id));
		}
	};

	const visibleLibraries = filterAndSortLibraries(libraries, query, sort, direction);

	let contentSection: React.ReactNode;
	if (loading) {
		contentSection = (
			<Box sx={{ display: "flex", justifyContent: "center", py: 8 }}>
				<CircularProgress sx={{ color: "var(--ds-red)" }} />
			</Box>
		);
	} else if (libraries.length === 0) {
		contentSection = (
			<Card
				sx={{
					backgroundColor: "var(--ds-bg-overlay)",
					border: "1px dashed var(--ds-border-hi)",
					borderRadius: "4px",
					position: "relative",
					overflow: "hidden",
				}}
			>
				<HudFrame />
				<CardContent sx={{ textAlign: "center", py: 8, px: 3 }}>
					<Box
						sx={{
							width: 64,
							height: 64,
							mx: "auto",
							mb: 2,
							display: "flex",
							alignItems: "center",
							justifyContent: "center",
							backgroundColor: "var(--ds-bg-surface)",
							border: "1px solid var(--ds-border-red)",
							borderRadius: "4px",
						}}
					>
						<LibraryBooksIcon sx={{ fontSize: 32, color: "var(--ds-red-glow)" }} />
					</Box>
					<Typography
						component="h2"
						sx={{
							fontFamily: "'Orbitron', sans-serif",
							fontSize: "20px",
							fontWeight: 900,
							letterSpacing: "1.5px",
							color: "var(--ds-text-strong)",
							mb: 1,
						}}
					>
						NO HAY BIBLIOTECAS REGISTRADAS
					</Typography>
					<Typography
						variant="body2"
						sx={{
							fontFamily: "'Inter', sans-serif",
							color: "var(--ds-muted)",
							maxWidth: 480,
							mx: "auto",
							mb: 3,
						}}
					>
						Registra un nodo de biblioteca apuntando a la ruta local o compartida donde almacenas tus tomos de manga y novelas gráficas.
					</Typography>
					<Button
						variant="contained"
						startIcon={<AddIcon />}
						onClick={openForCreate}
						className="btn-tactical"
						sx={{
							px: 3,
							py: 1.2,
							background: "var(--ds-red)",
							borderColor: "var(--ds-red-glow)",
							color: "#FFFFFF",
						}}
					>
						NUEVA BIBLIOTECA
					</Button>
				</CardContent>
			</Card>
		);
	} else {
		contentSection = (
			<Box
				sx={{
					display: "grid",
					gap: 2,
					gridTemplateColumns: PANEL_GRID,
				}}
			>
				{visibleLibraries.map((library) => {
					const active = activeScans.find((scan) => scan.libraryId === library.id);
					const queued = active?.queued ?? false;
					const scanning = active !== undefined && !queued;
					const missing = library.missingSeries + library.missingVolumes;
					const busy = active !== undefined || scanningIds.includes(library.id);

					let statusDotColor = "var(--ds-ok)";
					let statusText = "READY";
					let statusBg = "rgba(var(--ds-ok-rgb), 0.1)";
					let statusBorder = "var(--ds-ok-dark)";

					if (scanning) {
						statusDotColor = "var(--ds-red-glow)";
						statusText = "SCANNING";
						statusBg = "rgba(var(--ds-red-rgb), 0.15)";
						statusBorder = "var(--ds-red-dark)";
					} else if ((library.status ?? "").toUpperCase() === "MISSING") {
						statusDotColor = "var(--ds-warn)";
						statusText = "MISSING";
						statusBg = "rgba(var(--ds-warn-rgb), 0.15)";
						statusBorder = "var(--ds-warn-dark)";
					}

					return (
						<Card
							key={library.id}
							sx={{
								display: "flex",
								flexDirection: "column",
								overflow: "hidden",
								transition: "transform 0.25s ease, filter 0.25s ease, opacity 0.25s ease",
								"&:hover": { transform: "translateY(-2px)" },
								...(queued && { filter: "grayscale(1)", opacity: 0.55 }),
							}}
						>
							<HudFrame />

							{busy && !queued && <Box className="ds-scanline" />}
							{queued && (
								<Box
									sx={{
										position: "absolute",
										top: 8,
										right: 8,
										zIndex: 3,
										fontFamily: "'JetBrains Mono', monospace",
										fontSize: "10px",
										letterSpacing: "1px",
										color: "var(--ds-subtle)",
										border: "1px solid var(--ds-border)",
										px: 0.75,
									}}
								>
									EN COLA
								</Box>
							)}

							{missing > 0 && !scanning && (
								<Tooltip title={`${String(missing)} entradas del índice ya no están en el disco`}>
									<InfoOutlinedIcon
										sx={{
											position: "absolute",
											top: 8,
											right: 8,
											zIndex: 3,
											fontSize: 18,
											color: "var(--ds-warn)",
										}}
									/>
								</Tooltip>
							)}

							{active && !queued && (
								<Box
									sx={{
										position: "absolute",
										top: 8,
										right: 8,
										maxWidth: "70%",
										zIndex: 3,
										fontFamily: "'JetBrains Mono', monospace",
										fontSize: "10px",
										letterSpacing: "1px",
										color: "var(--ds-red-glow)",
										border: "1px solid var(--ds-red-dark)",
										backgroundColor: "var(--ds-bg-sunken)",
										px: 0.75,
										overflow: "hidden",
										textOverflow: "ellipsis",
										whiteSpace: "nowrap",
									}}
								>
									{scanLabel(active)}
								</Box>
							)}

							<CardContent sx={{ p: 2.5, flexGrow: 1, display: "flex", flexDirection: "column" }}>
								<Box sx={{ display: "flex", flexDirection: "column", gap: 1.25, mb: 2 }}>
									<Box sx={{ display: "flex", alignItems: "center", gap: 1.5 }}>
										<Box
											sx={{
												width: 42,
												height: 42,
												flexShrink: 0,
												display: "flex",
												alignItems: "center",
												justifyContent: "center",
												background: "linear-gradient(145deg, var(--ds-red-deep) 0%, var(--ds-bg-deep) 100%)",
												border: "1px solid var(--ds-border-red)",
												borderRadius: "3px",
											}}
										>
											<LibraryBooksIcon sx={{ color: "var(--ds-red-glow)", fontSize: 22 }} />
										</Box>

										<Typography
											component={RouterLink}
											to={`/libraries/${library.id}`}
											noWrap
											sx={{
												flexGrow: 1,
												minWidth: 0,
												fontFamily: "'Rajdhani', sans-serif",
												fontSize: "20px",
												fontWeight: 700,
												letterSpacing: "1px",
												color: "var(--ds-text-strong)",
												textTransform: "uppercase",
												textDecoration: "none",
												transition: "color 0.2s ease",
												"&:hover": { color: "var(--ds-red-glow)" },
											}}
										>
											{library.name}
										</Typography>
									</Box>

								<Stack direction="row" spacing={0.5} sx={{ flexWrap: "wrap", gap: 0.5 }}>
									<Tooltip
										title={
											missing > 0
												? `${String(missing)} entradas sin carpeta: entra y pulsa REVISAR`
												: "Ver series y tomos"
										}
									>
										<IconButton
											size="small"
											component={RouterLink}
											to={`/libraries/${library.id}`}
											className={missing > 0 ? "ds-attention" : undefined}
											sx={{
												color: missing > 0 ? "var(--ds-warn)" : "var(--ds-text-2)",
												border: "1px solid var(--ds-border)",
												borderRadius: "2px",
												p: 0.6,
												"&:hover": {
													color: "var(--ds-red-glow)",
													borderColor: "var(--ds-red)",
													backgroundColor: "rgba(var(--ds-red-rgb), 0.1)",
												},
											}}
										>
											<ChevronRightIcon fontSize="small" />
										</IconButton>
									</Tooltip>

									<Tooltip title={busy ? "Escaneo en curso..." : "Lanzar escaneo manual"}>
										<span>
											<IconButton
												size="small"
												onClick={() => { void handleScan(library); }}
												disabled={busy}
												sx={{
													color: "var(--ds-text-2)",
													border: "1px solid var(--ds-border)",
													borderRadius: "2px",
													p: 0.6,
													"&:hover": {
														color: "var(--ds-red-glow)",
														borderColor: "var(--ds-red)",
														backgroundColor: "rgba(var(--ds-red-rgb), 0.1)",
													},
												}}
											>
												<SyncIcon
													fontSize="small"
													sx={{
														color: busy ? "var(--ds-warn)" : "inherit",
														animation: busy ? "spin 1.2s linear infinite" : "none",
														"@keyframes spin": {
															"0%": { transform: "rotate(0deg)" },
															"100%": { transform: "rotate(360deg)" },
														},
													}}
												/>
											</IconButton>
										</span>
									</Tooltip>

									<Tooltip title="Subir ficheros">
										<IconButton
											size="small"
											onClick={() => {
												setUploadTarget({ library, subpath: "" });
											}}
											sx={{
												color: "var(--ds-text-2)",
												border: "1px solid var(--ds-border)",
												borderRadius: "2px",
												p: 0.6,
												"&:hover": {
													color: "var(--ds-text-strong)",
													borderColor: "var(--ds-border-hi)",
													backgroundColor: "var(--ds-bg-surface)",
												},
											}}
										>
											<CloudUploadIcon fontSize="small" />
										</IconButton>
									</Tooltip>

									<Tooltip title="Editar configuración">
										<IconButton
											size="small"
											onClick={() => openForEdit(library)}
											sx={{
												color: "var(--ds-text-2)",
												border: "1px solid var(--ds-border)",
												borderRadius: "2px",
												p: 0.6,
												"&:hover": {
													color: "var(--ds-text-strong)",
													borderColor: "var(--ds-border-hi)",
													backgroundColor: "var(--ds-bg-surface)",
												},
											}}
										>
											<EditOutlinedIcon fontSize="small" />
										</IconButton>
									</Tooltip>

									<Tooltip title="Eliminar del índice">
										<IconButton
											size="small"
											onClick={() => {
													setDeleteFiles(false);
													setPendingDelete(library);
												}}
											sx={{
												color: "var(--ds-text-2)",
												border: "1px solid var(--ds-border)",
												borderRadius: "2px",
												p: 0.6,
												"&:hover": {
													color: "var(--ds-red-glow)",
													borderColor: "var(--ds-red-dark)",
													backgroundColor: "rgba(var(--ds-red-rgb), 0.15)",
												},
											}}
										>
											<DeleteOutlinedIcon fontSize="small" />
										</IconButton>
									</Tooltip>
								</Stack>

									<Box sx={{ display: "flex", alignItems: "center", gap: 0.5 }}>
										<Tooltip title={library.path}>
											<Box
												sx={{
													display: "flex",
													alignItems: "center",
													gap: 0.5,
													flexGrow: 1,
													minWidth: 0,
													backgroundColor: "var(--ds-bg-deep)",
													border: "1px solid var(--ds-bg-surface-hi)",
													borderRadius: "2px",
													px: 0.75,
													py: 0.2,
												}}
											>
												<FolderOpenIcon sx={{ fontSize: 12, color: "var(--ds-red-light)", flexShrink: 0 }} />
												<Typography
													noWrap
													sx={{ fontFamily: "'JetBrains Mono', monospace", fontSize: "11px", color: "var(--ds-muted)" }}
												>
													{library.path}
												</Typography>
											</Box>
										</Tooltip>

										<Tooltip title="Ver las carpetas que contiene">
											<IconButton
												size="small"
												onClick={() => {
													setExpanded((prev) => (prev === library.id ? null : library.id));
												}}
												sx={{ color: "var(--ds-text-2)", p: 0.4 }}
											>
												<ExpandMoreIcon
													fontSize="small"
													sx={{
														transition: "transform 0.2s ease",
														transform: expanded === library.id ? "rotate(180deg)" : "none",
													}}
												/>
											</IconButton>
										</Tooltip>
									</Box>

									<Collapse in={expanded === library.id} unmountOnExit>
										<LibraryFolderList
											library={library}
											onUpload={(subpath) => {
												setUploadTarget({ library, subpath });
											}}
										/>
									</Collapse>
								</Box>

								<Box
									sx={{
										display: "grid",
										gridTemplateColumns: "repeat(3, 1fr)",
										gap: 1,
										backgroundColor: "var(--ds-bg-sunken)",
										border: "1px solid var(--ds-bg-surface-hi)",
										borderRadius: "3px",
										p: 1.25,
										mb: 2,
										textAlign: "center",
									}}
								>
									<Box>
										<Box sx={{ display: "flex", alignItems: "center", justifyContent: "center", gap: 0.5, color: "var(--ds-subtle)" }}>
											<CollectionsBookmarkIcon sx={{ fontSize: 13 }} />
											<Typography sx={{ fontFamily: "'Rajdhani', sans-serif", fontSize: "10px", fontWeight: 700, letterSpacing: "1px" }}>
												SERIES
											</Typography>
										</Box>
										<Typography
											sx={{
												fontFamily: "'Orbitron', sans-serif",
												fontSize: "16px",
												fontWeight: 700,
												color: "var(--ds-text-strong)",
												mt: 0.25,
											}}
										>
											{library.seriesCount ?? 0}
										</Typography>
									</Box>

									<Box sx={{ borderLeft: "1px solid var(--ds-border-soft)", borderRight: "1px solid var(--ds-border-soft)" }}>
										<Box sx={{ display: "flex", alignItems: "center", justifyContent: "center", gap: 0.5, color: "var(--ds-subtle)" }}>
											<AutoStoriesIcon sx={{ fontSize: 13 }} />
											<Typography sx={{ fontFamily: "'Rajdhani', sans-serif", fontSize: "10px", fontWeight: 700, letterSpacing: "1px" }}>
												TOMOS
											</Typography>
										</Box>
										<Typography
											sx={{
												fontFamily: "'Orbitron', sans-serif",
												fontSize: "16px",
												fontWeight: 700,
												color: "var(--ds-red-light)",
												mt: 0.25,
											}}
										>
											{library.mediaCount ?? 0}
										</Typography>
									</Box>

									<Box>
										<Typography sx={{ fontFamily: "'Rajdhani', sans-serif", fontSize: "10px", fontWeight: 700, letterSpacing: "1px", color: "var(--ds-subtle)" }}>
											TIPO
										</Typography>
										<Typography
											sx={{
												fontFamily: "'Rajdhani', sans-serif",
												fontSize: "14px",
												fontWeight: 700,
												color: "var(--ds-platinum)",
												letterSpacing: "0.5px",
												mt: 0.25,
											}}
										>
											{library.config?.libraryType ?? "MIXED"}
										</Typography>
									</Box>
								</Box>

								{library.description && (
									<Typography
										variant="body2"
										sx={{
											fontFamily: "'Inter', sans-serif",
											fontSize: "12px",
											color: "var(--ds-muted)",
											lineHeight: 1.4,
											mb: 2,
											flexGrow: 1,
										}}
									>
										{library.description}
									</Typography>
								)}

								<Box
									sx={{
										mt: "auto",
										pt: 1.5,
										borderTop: "1px solid var(--ds-metal-3)",
										display: "flex",
										alignItems: "center",
										justifyContent: "space-between",
									}}
								>
									<Box
										sx={{
											display: "inline-flex",
											alignItems: "center",
											gap: 0.75,
											backgroundColor: statusBg,
											border: `1px solid ${statusBorder}`,
											borderRadius: "2px",
											px: 1,
											py: 0.3,
										}}
									>
										<Box
											sx={{
												width: 6,
												height: 6,
												borderRadius: "50%",
												backgroundColor: statusDotColor,
												boxShadow: `0 0 6px ${statusDotColor}`,
												animation: scanning ? "pulse 1.5s infinite" : "none",
												"@keyframes pulse": {
													"0%": { opacity: 1 },
													"50%": { opacity: 0.3 },
													"100%": { opacity: 1 },
												},
											}}
										/>
										<Typography
											sx={{
												fontFamily: "'JetBrains Mono', monospace",
												fontSize: "10px",
												fontWeight: 700,
												color: statusDotColor,
												letterSpacing: "0.5px",
											}}
										>
											{statusText}
										</Typography>
									</Box>

									<Typography
										sx={{
											fontFamily: "'JetBrains Mono', monospace",
											fontSize: "10px",
											color: busy ? "var(--ds-warn)" : "var(--ds-subtle)",
										}}
									>
										{!busy && formatDate(library.lastScannedAt)}
										{busy && queued && "en cola"}
										{busy && !queued && active?.etaSeconds !== undefined && `quedan ~${formatDuration(active.etaSeconds)}`}
										{busy && !queued && active?.etaSeconds === undefined && (
											<>
												{"calculando ETA"}
												<Box component="span" className="ds-dots">
													<span>.</span>
													<span>.</span>
													<span>.</span>
												</Box>
											</>
										)}
									</Typography>
								</Box>
							</CardContent>
						</Card>
					);
				})}
			</Box>
		);
	}

	let uploadDialog: React.ReactNode = null;
	if (uploadTarget) {
		const subpathLabel = uploadTarget.subpath ? ` / ${uploadTarget.subpath}` : "";
		const uploadDialogTitle = `// Subir a ${uploadTarget.library.name}${subpathLabel}`;
		uploadDialog = (
			<Dialog
				open
				onClose={() => {
					setUploadTarget(null);
				}}
				maxWidth="md"
				fullWidth
			>
				<HudFrame />
				<DialogTitle>{uploadDialogTitle}</DialogTitle>
				<DialogContent sx={{ p: 3 }}>
					<UploadPanel
						libraryId={uploadTarget.library.id}
						libraryPath={uploadTarget.library.path}
						initialSubpath={uploadTarget.subpath}
					/>
				</DialogContent>
				<DialogActions>
					<Button
						onClick={() => {
							setUploadTarget(null);
							void load(true);
						}}
					>
						Cerrar
					</Button>
				</DialogActions>
			</Dialog>
		);
	}

	return (
		<Box>
			<PageHeader
				title="Bibliotecas de Archivo"
				subtitle="Carpetas que el motor DiarSpeicher indexa y sirve a los visores de manga y literatura."
				actions={
					<>
						<Button
							onClick={() => { void load(); }}
							disabled={loading}
							startIcon={<RefreshIcon />}
							sx={{
								color: "var(--ds-text-2)",
								border: "1px solid var(--ds-border)",
								backgroundColor: "var(--ds-bg-sunken)",
								"&:hover": { borderColor: "var(--ds-border-hi)", backgroundColor: "var(--ds-bg-surface)", color: "var(--ds-text-strong)" },
							}}
						>
							ACTUALIZAR
						</Button>
						<Button
							variant="contained"
							startIcon={<AddIcon />}
							onClick={openForCreate}
							className="btn-tactical"
							sx={{
								background: "var(--ds-red)",
								borderColor: "var(--ds-red-glow)",
								color: "#FFFFFF",
							}}
						>
							NUEVA BIBLIOTECA
						</Button>
					</>
				}
			/>

			{error && (
				<Alert
					severity="error"
					onClose={() => setError(null)}
					sx={{
						mb: 3,
						backgroundColor: "var(--ds-red-deep)",
						border: "1px solid var(--ds-red-dark)",
						borderLeft: "4px solid var(--ds-red)",
						color: "var(--ds-red-soft)",
					}}
				>
					{error}
				</Alert>
			)}

			{notice && (
				<Alert
					severity="success"
					onClose={() => setNotice(null)}
					sx={{
						mb: 3,
						backgroundColor: "var(--ds-ok-bg)",
						border: "1px solid var(--ds-ok-dark)",
						borderLeft: "4px solid var(--ds-ok)",
						color: "var(--ds-ok-light)",
					}}
				>
					{notice}
				</Alert>
			)}

			{!loading && libraries.length > 0 && (
				<CatalogToolbar
					query={query}
					onQueryChange={setQuery}
					sort={sort}
					onSortChange={setSort}
					direction={direction}
					onDirectionChange={setDirection}
					options={LIBRARY_SORTS}
					placeholder="Buscar por nombre, ruta o descripción..."
					shown={visibleLibraries.length}
					total={libraries.length}
				/>
			)}

			{contentSection}

			<LibraryFormDialog
				open={formOpen}
				editing={editing}
				onClose={() => setFormOpen(false)}
				onSaved={(msg) => {
					setNotice(msg);
					void load(true);
				}}
			/>

			<Dialog
				open={pendingDelete !== null}
				onClose={() => !saving && setPendingDelete(null)}
				slotProps={{
					paper: {
						sx: {
							backgroundColor: "var(--ds-bg-overlay)",
							border: "1px solid var(--ds-red-dark)",
							position: "relative",
						},
					},
				}}
			>
				<HudFrame />

				<DialogTitle
					sx={{
						fontFamily: "'Orbitron', sans-serif",
						fontSize: "17px",
						fontWeight: 900,
						color: "var(--ds-red-glow)",
						backgroundColor: "var(--ds-bg-danger)",
						borderBottom: "1px solid var(--ds-border-red)",
					}}
				>
					CONFIRMAR ELIMINACIÓN DE ÍNDICE
				</DialogTitle>

				<DialogContent sx={{ p: 3, mt: 1 }}>
					<Typography sx={{ fontFamily: "'Inter', sans-serif", color: "var(--ds-platinum)", mb: 2 }}>
						¿Estás seguro de que deseas eliminar la biblioteca <strong>{pendingDelete?.name}</strong>?
					</Typography>
					{!deleteFiles && (
						<Box
							sx={{
								p: 1.5,
								backgroundColor: "var(--ds-bg-panel)",
								borderLeft: "3px solid var(--ds-ok)",
								borderRadius: "2px",
							}}
						>
							<Typography sx={{ fontFamily: "'Inter', sans-serif", fontSize: "12px", color: "var(--ds-muted)" }}>
								<strong>Aviso de seguridad:</strong> Los archivos originales de manga y libros alojados en el disco
								nunca serán eliminados ni modificados. Solo se purgará el índice de metadatos en la base de datos de
								DiarSpeicher.
							</Typography>
						</Box>
					)}

					<FormControlLabel
						sx={{ mt: 2 }}
						control={
							<Switch
								checked={deleteFiles}
								onChange={(e) => {
									setDeleteFiles(e.target.checked);
								}}
								disabled={saving}
							/>
						}
						label={
							<Typography sx={{ fontFamily: "'Rajdhani', sans-serif", fontWeight: 700, letterSpacing: "1px" }}>
								ELIMINAR TAMBIÉN EL DIRECTORIO DEL DISCO
							</Typography>
						}
					/>

					{deleteFiles && pendingDelete && <DeleteScopeNotice path={pendingDelete.path} />}
				</DialogContent>

				<DialogActions
					sx={{
						p: 2,
						borderTop: "1px solid var(--ds-border-soft)",
						backgroundColor: "var(--ds-bg-sunken)",
					}}
				>
					<Button
						onClick={() => {
							setDeleteFiles(false);
							setPendingDelete(null);
						}}
						disabled={saving}
						sx={{ color: "var(--ds-muted)" }}
					>
						CANCELAR
					</Button>
					<Button
						onClick={() => { void handleDelete(); }}
						variant="contained"
						disabled={saving}
						sx={{
							backgroundColor: "var(--ds-red)",
							color: "#FFFFFF",
							"&:hover": { backgroundColor: "var(--ds-red-hover)" },
						}}
					>
						{saving ? "ELIMINANDO..." : "ELIMINAR DEL ÍNDICE"}
					</Button>
				</DialogActions>
			</Dialog>

			{uploadDialog}
		</Box>
	);
}

function LibraryFolderList({
	library,
	onUpload,
}: Readonly<{ library: LibraryItem; onUpload: (subpath: string) => void }>) {
	const [entries, setEntries] = useState<FolderEntry[] | null>(null);

	useEffect(() => {
		let cancelled = false;
		filesystemApi.browse(library.path).then(
			(listing) => {
				if (!cancelled) setEntries(listing.entries);
			},
			() => {
				if (!cancelled) setEntries([]);
			},
		);

		return () => {
			cancelled = true;
		};
	}, [library.path]);

	if (entries === null) {
		return <LinearProgress sx={{ height: 2 }} />;
	}

	if (entries.length === 0) {
		return (
			<Typography sx={{ fontFamily: "'JetBrains Mono', monospace", fontSize: "11px", color: "var(--ds-subtle)", px: 1 }}>
				Sin carpetas dentro.
			</Typography>
		);
	}

	return (
		<Box sx={{ maxHeight: 190, overflowY: "auto", border: "1px solid var(--ds-border-soft)" }}>
			{entries.map((entry) => (
				<Box
					key={entry.path}
					sx={{
						display: "flex",
						alignItems: "center",
						gap: 1,
						px: 1,
						py: 0.6,
						borderBottom: "1px solid var(--ds-border-soft)",
						"&:hover": { backgroundColor: "rgba(var(--ds-red-rgb), 0.06)" },
					}}
				>
					<FolderIcon sx={{ fontSize: 15, color: "var(--ds-red-light)", flexShrink: 0 }} />
					<Typography
						noWrap
						sx={{ flexGrow: 1, minWidth: 0, fontFamily: "'Rajdhani', sans-serif", fontSize: "13px", fontWeight: 600 }}
					>
						{entry.name}
					</Typography>
					{entry.fileCount > 0 && (
						<Typography sx={{ fontFamily: "'JetBrains Mono', monospace", fontSize: "10px", color: "var(--ds-subtle)" }}>
							{entry.fileCount}
						</Typography>
					)}
					<Tooltip title={`Subir a ${entry.name}`}>
						<IconButton
							size="small"
							onClick={() => {
								onUpload(entry.name);
							}}
							sx={{ color: "var(--ds-text-2)", p: 0.3 }}
						>
							<CloudUploadIcon sx={{ fontSize: 16 }} />
						</IconButton>
					</Tooltip>
				</Box>
			))}
		</Box>
	);
}
