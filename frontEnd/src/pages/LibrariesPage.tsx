import {
	Accordion,
	AccordionDetails,
	AccordionSummary,
	Alert,
	Box,
	Button,
	Card,
	CardContent,
	CircularProgress,
	Dialog,
	DialogActions,
	DialogContent,
	DialogTitle,
	IconButton,
	LinearProgress,
	Stack,
	TextField,
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
	librariesApi,
	DEFAULT_LIBRARY_CONFIG,
	LIBRARY_STATUS_SCANNING,
	type LibraryConfig,
	type LibraryItem,
} from "../api/endpoints";
import LibraryConfigForm from "../components/LibraryConfigForm";

const SCAN_POLL_MS = 3000;

function isScanning(library: LibraryItem): boolean {
	return (library.status ?? "").toUpperCase() === LIBRARY_STATUS_SCANNING;
}

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

export default function LibrariesPage() {
	const [libraries, setLibraries] = useState<LibraryItem[]>([]);
	const [loading, setLoading] = useState(true);
	const [error, setError] = useState<string | null>(null);
	const [notice, setNotice] = useState<string | null>(null);
	const [scanningIds, setScanningIds] = useState<string[]>([]);

	const [query, setQuery] = useState("");
	const [sort, setSort] = useState("name");
	const [direction, setDirection] = useState<SortDirection>("asc");

	const [editing, setEditing] = useState<LibraryItem | null>(null);
	const [formOpen, setFormOpen] = useState(false);
	const [name, setName] = useState("");
	const [path, setPath] = useState("");
	const [description, setDescription] = useState("");
	const [emoji, setEmoji] = useState("");
	const [config, setConfig] = useState<LibraryConfig>(DEFAULT_LIBRARY_CONFIG);
	const [saving, setSaving] = useState(false);
	const [pendingDelete, setPendingDelete] = useState<LibraryItem | null>(null);

	const pollRef = useRef<number | null>(null);

	const load = useCallback(async (silent = false) => {
		if (!silent) setLoading(true);
		try {
			const list = await librariesApi.list();
			setLibraries(list ?? []);
			if (!silent) setError(null);
		} catch (err: unknown) {
			setError(err instanceof Error ? err.message : "Error cargando las bibliotecas.");
		} finally {
			if (!silent) setLoading(false);
		}
	}, []);

	useEffect(() => {
		let isMounted = true;
		const init = async () => {
			try {
				const list = await librariesApi.list();
				if (isMounted) {
					setLibraries(list ?? []);
				}
			} catch (err: unknown) {
				if (isMounted) {
					setError(err instanceof Error ? err.message : "Error cargando las bibliotecas.");
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
		const anyScanning = libraries.some(isScanning);
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
	}, [libraries, load]);

	const openForCreate = () => {
		setEditing(null);
		setName("");
		setPath("");
		setDescription("");
		setEmoji("");
		setConfig(DEFAULT_LIBRARY_CONFIG);
		setFormOpen(true);
	};

	const openForEdit = (library: LibraryItem) => {
		setEditing(library);
		setName(library.name);
		setPath(library.path);
		setDescription(library.description ?? "");
		setEmoji(library.emoji ?? "");
		setConfig(library.config ?? DEFAULT_LIBRARY_CONFIG);
		setFormOpen(true);
	};

	const handleSubmit = async (e: React.SyntheticEvent) => {
		e.preventDefault();
		if (!name.trim() || (!editing && !path.trim())) {
			setError("El nombre y la ruta de la biblioteca son obligatorios.");
			return;
		}

		setSaving(true);
		setError(null);
		try {
			if (editing) {
				await librariesApi.update(editing.id, {
					name: name.trim(),
					description: description.trim(),
					emoji: emoji.trim() || undefined,
					config,
				});
				setNotice(`Biblioteca "${name}" actualizada.`);
			} else {
				await librariesApi.create({
					name: name.trim(),
					path: path.trim(),
					description: description.trim(),
					emoji: emoji.trim() || undefined,
					config,
				});
				setNotice(`Biblioteca "${name}" dada de alta correctamente.`);
			}
			setFormOpen(false);
			await load(true);
		} catch (err: unknown) {
			setError(err instanceof Error ? err.message : "Error guardando la biblioteca.");
		} finally {
			setSaving(false);
		}
	};

	const handleDelete = async () => {
		if (!pendingDelete) return;
		setSaving(true);
		setError(null);
		try {
			await librariesApi.delete(pendingDelete.id);
			setNotice(`Biblioteca "${pendingDelete.name}" eliminada del índice.`);
			setPendingDelete(null);
			await load(true);
		} catch (err: unknown) {
			setError(err instanceof Error ? err.message : "Error eliminando la biblioteca.");
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
			setError(err instanceof Error ? err.message : "No se pudo encolar el escaneo.");
		} finally {
			setScanningIds((prev) => prev.filter((id) => id !== library.id));
		}
	};

	const visibleLibraries = filterAndSortLibraries(libraries, query, sort, direction);

	let contentSection: React.ReactNode;
	if (loading) {
		contentSection = (
			<Box sx={{ display: "flex", justifyContent: "center", py: 8 }}>
				<CircularProgress sx={{ color: "#C21818" }} />
			</Box>
		);
	} else if (libraries.length === 0) {
		contentSection = (
			<Card
				sx={{
					backgroundColor: "#0D0F14",
					border: "1px dashed #383E4C",
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
							backgroundColor: "#161821",
							border: "1px solid #381010",
							borderRadius: "4px",
						}}
					>
						<LibraryBooksIcon sx={{ fontSize: 32, color: "#FF2E2E" }} />
					</Box>
					<Typography
						component="h2"
						sx={{
							fontFamily: "'Orbitron', sans-serif",
							fontSize: "20px",
							fontWeight: 900,
							letterSpacing: "1.5px",
							color: "#FFFFFF",
							mb: 1,
						}}
					>
						NO HAY BIBLIOTECAS REGISTRADAS
					</Typography>
					<Typography
						variant="body2"
						sx={{
							fontFamily: "'Inter', sans-serif",
							color: "#8E95A5",
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
							background: "#C21818",
							borderColor: "#FF2E2E",
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
					const scanning = isScanning(library);
					const busy = scanning || scanningIds.includes(library.id);

					let statusDotColor = "#22C55E";
					let statusText = "READY";
					let statusBg = "rgba(34, 197, 94, 0.1)";
					let statusBorder = "#14532D";

					if (scanning) {
						statusDotColor = "#FF2E2E";
						statusText = "SCANNING";
						statusBg = "rgba(194, 24, 24, 0.15)";
						statusBorder = "#660B0B";
					} else if ((library.status ?? "").toUpperCase() === "MISSING") {
						statusDotColor = "#F59E0B";
						statusText = "MISSING";
						statusBg = "rgba(245, 158, 11, 0.15)";
						statusBorder = "#78350F";
					}

					return (
						<Card
							key={library.id}
							sx={{
								display: "flex",
								flexDirection: "column",
								overflow: "hidden",
								transition: "transform 0.25s ease",
								"&:hover": { transform: "translateY(-2px)" },
							}}
						>
							<HudFrame />

							{busy && (
								<LinearProgress
									sx={{
										height: 3,
										backgroundColor: "#1C0303",
										"& .MuiLinearProgress-bar": {
											backgroundColor: "#FF2E2E",
										},
									}}
								/>
							)}

							<CardContent sx={{ p: 2.5, flexGrow: 1, display: "flex", flexDirection: "column" }}>
								{/* Cabecera de la Tarjeta */}
								<Box sx={{ display: "flex", alignItems: "flex-start", gap: 1.5, mb: 2 }}>
									<Box
										sx={{
											width: 42,
											height: 42,
											flexShrink: 0,
											display: "flex",
											alignItems: "center",
											justifyContent: "center",
											background: "linear-gradient(145deg, #1C0303 0%, #0D0E12 100%)",
											border: "1px solid #381010",
											borderRadius: "3px",
										}}
									>
										<LibraryBooksIcon sx={{ color: "#FF2E2E", fontSize: 22 }} />
									</Box>

									<Box sx={{ minWidth: 0, flexGrow: 1 }}>
										<Box sx={{ display: "flex", alignItems: "center", gap: 1, mb: 0.25 }}>
											<Typography
												component={RouterLink}
												to={`/libraries/${library.id}`}
												noWrap
												sx={{
													display: "block",
													fontFamily: "'Rajdhani', sans-serif",
													fontSize: "18px",
													fontWeight: 700,
													letterSpacing: "1px",
													color: "#FFFFFF",
													textTransform: "uppercase",
													textDecoration: "none",
													transition: "color 0.2s ease",
													"&:hover": { color: "#FF2E2E" },
												}}
											>
												{library.name}
											</Typography>
										</Box>

										<Tooltip title={library.path}>
											<Box
												sx={{
													display: "inline-flex",
													alignItems: "center",
													gap: 0.5,
													maxWidth: "100%",
													backgroundColor: "#08090D",
													border: "1px solid #1F232D",
													borderRadius: "2px",
													px: 0.75,
													py: 0.2,
												}}
											>
												<FolderOpenIcon sx={{ fontSize: 12, color: "#FF3E3E", flexShrink: 0 }} />
												<Typography
													sx={{
														fontFamily: "'JetBrains Mono', monospace",
														fontSize: "11px",
														color: "#8E95A5",
														overflow: "hidden",
														textOverflow: "ellipsis",
														whiteSpace: "nowrap",
													}}
												>
													{library.path}
												</Typography>
											</Box>
										</Tooltip>
									</Box>

									{/* Botones de acción */}
									<Stack direction="row" spacing={0.5} sx={{ flexShrink: 0 }}>
										<Tooltip title="Ver series y tomos">
											<IconButton
												size="small"
												component={RouterLink}
												to={`/libraries/${library.id}`}
												sx={{
													color: "#A3ABB8",
													border: "1px solid #282C38",
													borderRadius: "2px",
													p: 0.6,
													"&:hover": {
														color: "#FF2E2E",
														borderColor: "#C21818",
														backgroundColor: "rgba(194, 24, 24, 0.1)",
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
														color: "#A3ABB8",
														border: "1px solid #282C38",
														borderRadius: "2px",
														p: 0.6,
														"&:hover": {
															color: "#FF2E2E",
															borderColor: "#C21818",
															backgroundColor: "rgba(194, 24, 24, 0.1)",
														},
													}}
												>
													<SyncIcon
														fontSize="small"
														sx={{
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

										<Tooltip title="Editar configuración">
											<IconButton
												size="small"
												onClick={() => openForEdit(library)}
												sx={{
													color: "#A3ABB8",
													border: "1px solid #282C38",
													borderRadius: "2px",
													p: 0.6,
													"&:hover": {
														color: "#FFFFFF",
														borderColor: "#383E4C",
														backgroundColor: "#161821",
													},
												}}
											>
												<EditOutlinedIcon fontSize="small" />
											</IconButton>
										</Tooltip>

										<Tooltip title="Eliminar del índice">
											<IconButton
												size="small"
												onClick={() => setPendingDelete(library)}
												sx={{
													color: "#A3ABB8",
													border: "1px solid #282C38",
													borderRadius: "2px",
													p: 0.6,
													"&:hover": {
														color: "#FF2E2E",
														borderColor: "#660B0B",
														backgroundColor: "rgba(194, 24, 24, 0.15)",
													},
												}}
											>
												<DeleteOutlinedIcon fontSize="small" />
											</IconButton>
										</Tooltip>
									</Stack>
								</Box>

								{/* Cuadrícula de Métricas Tácticas */}
								<Box
									sx={{
										display: "grid",
										gridTemplateColumns: "repeat(3, 1fr)",
										gap: 1,
										backgroundColor: "#0A0B0E",
										border: "1px solid #212530",
										borderRadius: "3px",
										p: 1.25,
										mb: 2,
										textAlign: "center",
									}}
								>
									<Box>
										<Box sx={{ display: "flex", alignItems: "center", justifyContent: "center", gap: 0.5, color: "#636B7C" }}>
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
												color: "#FFFFFF",
												mt: 0.25,
											}}
										>
											{library.seriesCount ?? 0}
										</Typography>
									</Box>

									<Box sx={{ borderLeft: "1px solid #1C1F28", borderRight: "1px solid #1C1F28" }}>
										<Box sx={{ display: "flex", alignItems: "center", justifyContent: "center", gap: 0.5, color: "#636B7C" }}>
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
												color: "#FF3E3E",
												mt: 0.25,
											}}
										>
											{library.mediaCount ?? 0}
										</Typography>
									</Box>

									<Box>
										<Typography sx={{ fontFamily: "'Rajdhani', sans-serif", fontSize: "10px", fontWeight: 700, letterSpacing: "1px", color: "#636B7C" }}>
											TIPO
										</Typography>
										<Typography
											sx={{
												fontFamily: "'Rajdhani', sans-serif",
												fontSize: "14px",
												fontWeight: 700,
												color: "#F0F2F6",
												letterSpacing: "0.5px",
												mt: 0.25,
											}}
										>
											{library.config?.libraryType ?? "MIXED"}
										</Typography>
									</Box>
								</Box>

								{/* Descripción si existe */}
								{library.description && (
									<Typography
										variant="body2"
										sx={{
											fontFamily: "'Inter', sans-serif",
											fontSize: "12px",
											color: "#8E95A5",
											lineHeight: 1.4,
											mb: 2,
											flexGrow: 1,
										}}
									>
										{library.description}
									</Typography>
								)}

								{/* Pie de tarjeta con estado y fecha */}
								<Box
									sx={{
										mt: "auto",
										pt: 1.5,
										borderTop: "1px solid #1A1D26",
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
											color: "#636B7C",
										}}
									>
										{formatDate(library.lastScannedAt)}
									</Typography>
								</Box>
							</CardContent>
						</Card>
					);
				})}
			</Box>
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
								color: "#A3ABB8",
								border: "1px solid #282C38",
								backgroundColor: "#0A0B0E",
								"&:hover": { borderColor: "#383E4C", backgroundColor: "#161821", color: "#FFFFFF" },
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
								background: "#C21818",
								borderColor: "#FF2E2E",
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
						backgroundColor: "#1C0303",
						border: "1px solid #660B0B",
						borderLeft: "4px solid #C21818",
						color: "#FF5C5C",
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
						backgroundColor: "#071A0E",
						border: "1px solid #14532D",
						borderLeft: "4px solid #22C55E",
						color: "#4ADE80",
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

			{/* Diálogo de Alta / Edición Táctico */}
			<Dialog
				open={formOpen}
				onClose={() => !saving && setFormOpen(false)}
				maxWidth="md"
				fullWidth
				slotProps={{
					paper: {
						sx: {
							backgroundColor: "#0D0F14",
							border: "1px solid #C21818",
							boxShadow: "0 12px 50px rgba(0,0,0,0.9)",
							position: "relative",
						},
					},
				}}
			>
				<HudFrame />

				<form onSubmit={(e) => { void handleSubmit(e); }} noValidate>
					<DialogTitle
						sx={{
							fontFamily: "'Orbitron', sans-serif",
							fontSize: "18px",
							fontWeight: 900,
							letterSpacing: "1.5px",
							borderBottom: "1px solid #232733",
							backgroundColor: "#0A0B0E",
							color: "#FFFFFF",
							py: 2,
						}}
					>
						{editing ? `// EDITAR BIBLIOTECA: ${editing.name}` : "// REGISTRAR NUEVA BIBLIOTECA DE ARCHIVO"}
					</DialogTitle>

					<DialogContent sx={{ p: 3 }}>
						<Stack spacing={2.5} sx={{ mt: 1 }}>
							<TextField
								label="Nombre de la biblioteca"
								value={name}
								onChange={(e) => setName(e.target.value)}
								required
								fullWidth
								disabled={saving}
								placeholder="Ej. Manga Seinen, Cómics DC, Novelas Ligeras"
							/>

							<TextField
								label="Ruta en el sistema de ficheros"
								value={path}
								onChange={(e) => setPath(e.target.value)}
								required
								fullWidth
								disabled={Boolean(editing) || saving}
								helperText={
									editing
										? "La ruta del sistema no puede modificarse tras la creación."
										: "Ruta absoluta o relativa dentro del volumen de almacenamiento."
								}
								placeholder="/libraries/manga"
								slotProps={{
									input: {
										sx: { fontFamily: "'JetBrains Mono', monospace", fontSize: "13px" },
									},
								}}
							/>

							<TextField
								label="Descripción opcional"
								value={description}
								onChange={(e) => setDescription(e.target.value)}
								fullWidth
								multiline
								rows={2}
								disabled={saving}
								placeholder="Metadatos o notas internas sobre el contenido de este almacén."
							/>

							<Accordion
								sx={{
									backgroundColor: "#0A0B0E",
									border: "1px solid #232733",
									borderRadius: "3px !important",
									"&:before": { display: "none" },
								}}
							>
								<AccordionSummary
									expandIcon={<ExpandMoreIcon sx={{ color: "#FF2E2E" }} />}
									sx={{
										fontFamily: "'Rajdhani', sans-serif",
										fontWeight: 700,
										fontSize: "14px",
										letterSpacing: "1px",
										color: "#FFFFFF",
										textTransform: "uppercase",
									}}
								>
									CONFIGURACIÓN AVANZADA DEL ESCÁNER Y LECTOR
								</AccordionSummary>
								<AccordionDetails sx={{ pt: 2, borderTop: "1px solid #1C1F28" }}>
									<LibraryConfigForm value={config} onChange={setConfig} disabled={saving} />
								</AccordionDetails>
							</Accordion>
						</Stack>
					</DialogContent>

					<DialogActions
						sx={{
							p: 2.5,
							borderTop: "1px solid #1C1F28",
							backgroundColor: "#0A0B0E",
							justifyContent: "space-between",
						}}
					>
						<Button
							onClick={() => setFormOpen(false)}
							disabled={saving}
							sx={{
								color: "#8E95A5",
								fontFamily: "'Rajdhani', sans-serif",
								fontWeight: 700,
								"&:hover": { color: "#FFFFFF" },
							}}
						>
							CANCELAR
						</Button>
						{(() => {
							let submitLabel = "CREAR NODO";
							if (saving) {
								submitLabel = "PROCESANDO...";
							} else if (editing) {
								submitLabel = "GUARDAR CAMBIOS";
							}
							return (
								<Button
									type="submit"
									variant="contained"
									disabled={saving}
									className="btn-tactical"
									sx={{
										background: "#C21818",
										borderColor: "#FF2E2E",
										color: "#FFFFFF",
										px: 3,
									}}
								>
									{submitLabel}
								</Button>
							);
						})()}
					</DialogActions>
				</form>
			</Dialog>

			{/* Diálogo de Confirmación de Borrado */}
			<Dialog
				open={pendingDelete !== null}
				onClose={() => !saving && setPendingDelete(null)}
				slotProps={{
					paper: {
						sx: {
							backgroundColor: "#0D0F14",
							border: "1px solid #660B0B",
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
						color: "#FF2E2E",
						backgroundColor: "#160303",
						borderBottom: "1px solid #381010",
					}}
				>
					CONFIRMAR ELIMINACIÓN DE ÍNDICE
				</DialogTitle>

				<DialogContent sx={{ p: 3, mt: 1 }}>
					<Typography sx={{ fontFamily: "'Inter', sans-serif", color: "#F0F2F6", mb: 2 }}>
						¿Estás seguro de que deseas eliminar la biblioteca <strong>{pendingDelete?.name}</strong>?
					</Typography>
					<Box
						sx={{
							p: 1.5,
							backgroundColor: "#11131C",
							borderLeft: "3px solid #22C55E",
							borderRadius: "2px",
						}}
					>
						<Typography sx={{ fontFamily: "'Inter', sans-serif", fontSize: "12px", color: "#8E95A5" }}>
							<strong>Aviso de seguridad:</strong> Los archivos originales de manga y libros alojados en el disco nunca serán eliminados ni modificados. Solo se purgará el índice de metadatos en la base de datos de DiarSpeicher.
						</Typography>
					</Box>
				</DialogContent>

				<DialogActions
					sx={{
						p: 2,
						borderTop: "1px solid #1C1F28",
						backgroundColor: "#0A0B0E",
					}}
				>
					<Button
						onClick={() => setPendingDelete(null)}
						disabled={saving}
						sx={{ color: "#8E95A5" }}
					>
						CANCELAR
					</Button>
					<Button
						onClick={() => { void handleDelete(); }}
						variant="contained"
						disabled={saving}
						sx={{
							backgroundColor: "#C21818",
							color: "#FFFFFF",
							"&:hover": { backgroundColor: "#80060A" },
						}}
					>
						{saving ? "ELIMINANDO..." : "ELIMINAR DEL ÍNDICE"}
					</Button>
				</DialogActions>
			</Dialog>
		</Box>
	);
}
