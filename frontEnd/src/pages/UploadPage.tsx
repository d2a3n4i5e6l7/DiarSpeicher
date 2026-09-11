import {
	Alert,
	Box,
	Button,
	Card,
	CardContent,
	Chip,
	Divider,
	FormControl,
	IconButton,
	InputLabel,
	LinearProgress,
	List,
	ListItem,
	MenuItem,
	Select,
	Stack,
	TextField,
	Typography,
	useTheme,
} from "@mui/material";
import CloudUploadIcon from "@mui/icons-material/CloudUpload";
import LibraryBooksIcon from "@mui/icons-material/LibraryBooks";
import DeleteOutlinedIcon from "@mui/icons-material/DeleteOutlined";
import CheckCircleIcon from "@mui/icons-material/CheckCircle";
import InsertDriveFileIcon from "@mui/icons-material/InsertDriveFile";
import PauseOutlinedIcon from "@mui/icons-material/PauseOutlined";
import PlayArrowOutlinedIcon from "@mui/icons-material/PlayArrowOutlined";
import React, { useCallback, useEffect, useRef, useState } from "react";
import { librariesApi, type LibraryItem } from "../api/endpoints";
import { TusUpload, type TusUploadStatus } from "../api/tusClient";
import { Link as RouterLink } from "react-router-dom";

function formatBytes(bytes: number, decimals = 2): string {
	if (bytes === 0) return "0 Bytes";
	const k = 1024;
	const dm = Math.max(0, decimals);
	const sizes = ["Bytes", "KB", "MB", "GB"];
	const i = Math.floor(Math.log(bytes) / Math.log(k));
	return `${Number.parseFloat((bytes / Math.pow(k, i)).toFixed(dm))} ${sizes[i]}`;
}

interface UploadQueueItem {
	id: string;
	file: File;
	tusUpload: TusUpload;
	status: TusUploadStatus;
	bytesUploaded: number;
	percentage: number;
	errorMessage?: string;
}

export default function UploadPage() {
	const theme = useTheme();
	const fileInputRef = useRef<HTMLInputElement>(null);

	const [libraries, setLibraries] = useState<LibraryItem[]>([]);
	const [selectedLibraryId, setSelectedLibraryId] = useState<string>("");
	const [subpath, setSubpath] = useState("");
	const [items, setItems] = useState<UploadQueueItem[]>([]);
	const [isDragging, setIsDragging] = useState(false);

	const [loadingLibraries, setLoadingLibraries] = useState(true);
	const [error, setError] = useState<string | null>(null);
	const [successCount, setSuccessCount] = useState<number | null>(null);

	useEffect(() => {
		let isMounted = true;
		const init = async () => {
			try {
				const list = await librariesApi.list();
				if (isMounted) {
					setLibraries(list || []);
					if (list && list.length > 0) {
						setSelectedLibraryId((prev) => prev || list[0].id);
					}
				}
			} catch (err: unknown) {
				if (isMounted) {
					setError(err instanceof Error ? err.message : "Error cargando las bibliotecas.");
				}
			} finally {
				if (isMounted) {
					setLoadingLibraries(false);
				}
			}
		};

		void init();

		return () => {
			isMounted = false;
		};
	}, []);

	const createQueueItem = useCallback(
		(file: File): UploadQueueItem => {
			const itemId = `${file.name}-${file.size}-${Date.now()}-${Math.random()}`;

			const tusUploadInstance = new TusUpload({
				file,
				libraryId: selectedLibraryId,
				subpath: subpath.trim() || undefined,
				onProgress: (uploaded, _total, pct) => {
					setItems((prev) =>
						prev.map((it) =>
							it.id === itemId
								? {
										...it,
										bytesUploaded: uploaded,
										percentage: pct,
										status: "uploading",
									}
								: it
						)
					);
				},
				onSuccess: () => {
					setItems((prev) =>
						prev.map((it) =>
							it.id === itemId
								? {
										...it,
										percentage: 100,
										bytesUploaded: file.size,
										status: "completed",
									}
								: it
						)
					);
					setSuccessCount((prev) => (prev ? prev + 1 : 1));
				},
				onError: (err) => {
					setItems((prev) =>
						prev.map((it) =>
							it.id === itemId
								? {
										...it,
										status: "error",
										errorMessage: err.message,
									}
								: it
						)
					);
				},
			});

			return {
				id: itemId,
				file,
				tusUpload: tusUploadInstance,
				status: "idle",
				bytesUploaded: 0,
				percentage: 0,
			};
		},
		[selectedLibraryId, subpath]
	);

	const handleAddFiles = (files: File[]) => {
		if (!selectedLibraryId) {
			setError("Selecciona primero una biblioteca de destino.");
			return;
		}
		setError(null);
		const newItems = files.map((f) => createQueueItem(f));
		setItems((prev) => [...prev, ...newItems]);
	};

	const handleFileSelect = (e: React.ChangeEvent<HTMLInputElement>) => {
		if (e.target.files && e.target.files.length > 0) {
			handleAddFiles(Array.from(e.target.files));
		}
	};

	const handleDragOver = (e: React.DragEvent) => {
		e.preventDefault();
		setIsDragging(true);
	};

	const handleDragLeave = () => {
		setIsDragging(false);
	};

	const handleDrop = (e: React.DragEvent) => {
		e.preventDefault();
		setIsDragging(false);
		if (e.dataTransfer.files && e.dataTransfer.files.length > 0) {
			handleAddFiles(Array.from(e.dataTransfer.files));
		}
	};

	const handleStartUpload = async (item: UploadQueueItem) => {
		if (item.status === "uploading" || item.status === "completed") return;

		setItems((prev) =>
			prev.map((it) =>
				it.id === item.id ? { ...it, status: "uploading", errorMessage: undefined } : it
			)
		);

		await item.tusUpload.start();
	};

	const handlePauseUpload = (item: UploadQueueItem) => {
		item.tusUpload.pause();
		setItems((prev) =>
			prev.map((it) => (it.id === item.id ? { ...it, status: "paused" } : it))
		);
	};

	const handleRemoveItem = async (item: UploadQueueItem) => {
		await item.tusUpload.cancel();
		setItems((prev) => prev.filter((it) => it.id !== item.id));
	};

	const handleStartAll = () => {
		for (const item of items) {
			if (item.status === "idle" || item.status === "paused" || item.status === "error") {
				void item.tusUpload.start();
			}
		}
	};

	const handlePauseAll = () => {
		for (const item of items) {
			if (item.status === "uploading") {
				item.tusUpload.pause();
			}
		}
		setItems((prev) =>
			prev.map((it) => (it.status === "uploading" ? { ...it, status: "paused" } : it))
		);
	};

	const handleClearAll = async () => {
		for (const item of items) {
			await item.tusUpload.cancel();
		}
		setItems([]);
		if (fileInputRef.current) {
			fileInputRef.current.value = "";
		}
	};

	const dropBorderColor = isDragging ? theme.palette.primary.main : theme.palette.divider;
	let dropBgColor = isDragging ? "action.hover" : "background.paper";
	if (theme.palette.mode === "dark") {
		dropBgColor = isDragging ? "rgba(99, 102, 241, 0.12)" : "rgba(30, 41, 59, 0.5)";
	}

	const hasUploading = items.some((it) => it.status === "uploading");
	const hasPausedOrIdle = items.some(
		(it) => it.status === "idle" || it.status === "paused" || it.status === "error"
	);

	return (
		<Box sx={{ maxWidth: 1000, mx: "auto", py: 3, px: 2 }}>
			<Stack spacing={3}>
				{/* Cabecera */}
				<Box>
					<Typography variant="h4" component="h1" sx={{ fontWeight: 700 }}>
						Subida de Ficheros Reanudable (TUS)
					</Typography>
					<Typography variant="body1" color="text.secondary" sx={{ mt: 0.5 }}>
						Sube cómics, libros y mangas con soporte de pausa, reanudación y tolerancia a micro-cortes.
					</Typography>
				</Box>

				{error && (
					<Alert severity="error" onClose={() => setError(null)}>
						{error}
					</Alert>
				)}

				{successCount !== null && (
					<Alert severity="success" icon={<CheckCircleIcon />} onClose={() => setSuccessCount(null)}>
						Ficheros completados ({successCount}). El escáner automático de DiarSpeicher se ha activado.
					</Alert>
				)}

				{/* Selección de Biblioteca y Destino */}
				<Card>
					<CardContent>
						<Typography variant="h6" sx={{ fontWeight: 600, mb: 2 }}>
							1. Seleccionar Biblioteca Destino
						</Typography>

						{loadingLibraries ? (
							<LinearProgress sx={{ my: 2 }} />
						) : (
							<Stack direction={{ xs: "column", sm: "row" }} spacing={2} sx={{ alignItems: "center" }}>
								<FormControl size="small" sx={{ minWidth: 260, flexGrow: 1 }}>
									<InputLabel id="library-select-label">Biblioteca</InputLabel>
									<Select
										labelId="library-select-label"
										label="Biblioteca"
										value={selectedLibraryId}
										onChange={(e) => setSelectedLibraryId(e.target.value)}
										disabled={hasUploading}
									>
										{libraries.map((lib) => (
											<MenuItem key={lib.id} value={lib.id}>
												{lib.name} ({lib.path})
											</MenuItem>
										))}
									</Select>
								</FormControl>

								<Button
									variant="outlined"
									startIcon={<LibraryBooksIcon />}
									component={RouterLink}
									to="/libraries"
								>
									Gestionar bibliotecas
								</Button>

								<TextField
									label="Subcarpeta (opcional)"
									size="small"
									value={subpath}
									onChange={(e) => setSubpath(e.target.value)}
									disabled={hasUploading}
									placeholder="ej. Tomo 1"
									sx={{ flexGrow: 1 }}
								/>
							</Stack>
						)}
					</CardContent>
				</Card>

				{/* Zona Drag and Drop */}
				<Card>
					<CardContent>
						<Typography variant="h6" sx={{ fontWeight: 600, mb: 2 }}>
							2. Seleccionar o Arrastrar Ficheros
						</Typography>

						<Box
							onDragOver={handleDragOver}
							onDragLeave={handleDragLeave}
							onDrop={handleDrop}
							onClick={() => fileInputRef.current?.click()}
							sx={{
								border: `2px dashed ${dropBorderColor}`,
								borderRadius: 2,
								p: 4,
								textAlign: "center",
								backgroundColor: dropBgColor,
								cursor: "pointer",
								transition: "all 0.2s ease-in-out",
								"&:hover": {
									borderColor: theme.palette.primary.main,
									backgroundColor:
										theme.palette.mode === "dark"
											? "rgba(99, 102, 241, 0.05)"
											: "rgba(79, 70, 229, 0.02)",
								},
							}}
						>
							<input
								type="file"
								multiple
								ref={fileInputRef}
								style={{ display: "none" }}
								onChange={handleFileSelect}
								accept=".cbz,.cbr,.epub,.pdf,.zip"
							/>
							<CloudUploadIcon sx={{ fontSize: 48, color: "primary.main", mb: 1 }} />
							<Typography variant="body1" sx={{ fontWeight: 600 }}>
								Haz clic o arrastra ficheros aquí para subirlos
							</Typography>
							<Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
								Formatos compatibles: CBZ, CBR, EPUB, PDF, ZIP (Streaming Directo TUS)
							</Typography>
						</Box>

						{/* Cola de Subidas Granular */}
						{items.length > 0 && (
							<Box sx={{ mt: 3 }}>
								<Stack direction="row" sx={{ justifyContent: "space-between", alignItems: "center", mb: 2 }}>
									<Typography variant="subtitle1" sx={{ fontWeight: 700 }}>
										Cola de Transferencia ({items.length})
									</Typography>
									<Stack direction="row" spacing={1}>
										{hasPausedOrIdle && (
											<Button
												size="small"
												variant="contained"
												startIcon={<PlayArrowOutlinedIcon />}
												onClick={() => {
													handleStartAll();
												}}
											>
												Subir Todos
											</Button>
										)}
										{hasUploading && (
											<Button
												size="small"
												variant="outlined"
												color="warning"
												startIcon={<PauseOutlinedIcon />}
												onClick={handlePauseAll}
											>
												Pausar Todos
											</Button>
										)}
										<Button
											size="small"
											color="error"
											onClick={() => {
												void handleClearAll();
											}}
										>
											Limpiar Todos
										</Button>
									</Stack>
								</Stack>

								<List sx={{ border: 1, borderColor: "divider", borderRadius: 1.5, p: 0 }}>
									{items.map((item, idx) => {
										let statusChipColor: "default" | "primary" | "warning" | "success" | "error" = "default";
										let statusText = "En cola";

										if (item.status === "uploading") {
											statusChipColor = "primary";
											statusText = `Subiendo (${item.percentage}%)`;
										} else if (item.status === "paused") {
											statusChipColor = "warning";
											statusText = `Pausado (${item.percentage}%)`;
										} else if (item.status === "completed") {
											statusChipColor = "success";
											statusText = "Completado";
										} else if (item.status === "error") {
											statusChipColor = "error";
											statusText = "Error";
										}

										return (
											<React.Fragment key={item.id}>
												{idx > 0 && <Divider />}
												<ListItem sx={{ py: 1.5, display: "block" }}>
													<Stack spacing={1}>
														<Stack direction="row" sx={{ alignItems: "center", justifyContent: "space-between" }}>
															<Stack direction="row" spacing={1} sx={{ alignItems: "center", minWidth: 0 }}>
																<InsertDriveFileIcon color="action" fontSize="small" />
																<Typography variant="body2" sx={{ fontWeight: 600 }} noWrap>
																	{item.file.name}
																</Typography>
																<Typography variant="caption" color="text.secondary">
																	({formatBytes(item.file.size)})
																</Typography>
															</Stack>

															<Stack direction="row" spacing={1} sx={{ alignItems: "center" }}>
																<Chip
																	label={statusText}
																	size="small"
																	color={statusChipColor}
																	variant={item.status === "idle" ? "outlined" : "filled"}
																/>

																{item.status === "uploading" && (
																	<IconButton
																		size="small"
																		color="warning"
																		title="Pausar subida"
																		onClick={() => handlePauseUpload(item)}
																	>
																		<PauseOutlinedIcon fontSize="small" />
																	</IconButton>
																)}

																{(item.status === "paused" || item.status === "idle" || item.status === "error") && (
																	<IconButton
																		size="small"
																		color="primary"
																		title="Reanudar o Iniciar subida"
																		onClick={() => {
																			void handleStartUpload(item);
																		}}
																	>
																		<PlayArrowOutlinedIcon fontSize="small" />
																	</IconButton>
																)}

																<IconButton
																	size="small"
																	color="error"
																	title="Cancelar y eliminar"
																	onClick={() => {
																		void handleRemoveItem(item);
																	}}
																>
																	<DeleteOutlinedIcon fontSize="small" />
																</IconButton>
															</Stack>
														</Stack>

														{/* Barra de progreso */}
														<Box sx={{ width: "100%" }}>
															<LinearProgress
																variant="determinate"
																value={item.percentage}
																color={item.status === "error" ? "error" : "primary"}
																sx={{ height: 6, borderRadius: 3 }}
															/>
															<Stack direction="row" sx={{ justifyContent: "space-between", mt: 0.5 }}>
																<Typography variant="caption" color="text.secondary">
																	{formatBytes(item.bytesUploaded)} / {formatBytes(item.file.size)}
																</Typography>
																<Typography variant="caption" color="text.secondary">
																	{item.percentage}%
																</Typography>
															</Stack>
														</Box>

														{item.errorMessage && (
															<Typography variant="caption" color="error">
																{item.errorMessage}
															</Typography>
														)}
													</Stack>
												</ListItem>
											</React.Fragment>
										);
									})}
								</List>
							</Box>
						)}
					</CardContent>
				</Card>
			</Stack>

		</Box>
	);
}
