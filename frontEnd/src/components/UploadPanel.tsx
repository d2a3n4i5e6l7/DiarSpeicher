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
} from "@mui/material";
import CloudUploadIcon from "@mui/icons-material/CloudUpload";
import CreateNewFolderIcon from "@mui/icons-material/CreateNewFolder";
import DeleteOutlinedIcon from "@mui/icons-material/DeleteOutlined";
import CheckCircleIcon from "@mui/icons-material/CheckCircle";
import InsertDriveFileIcon from "@mui/icons-material/InsertDriveFile";
import PauseOutlinedIcon from "@mui/icons-material/PauseOutlined";
import PlayArrowOutlinedIcon from "@mui/icons-material/PlayArrowOutlined";
import React, { useCallback, useEffect, useRef, useState } from "react";
import { filesystemApi, type DiskUsage, type FolderEntry } from "../api/endpoints";
import { TusUpload, type TusUploadStatus } from "../api/tusClient";
import HudFrame from "./HudFrame";

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

interface Props {
	libraryId: string;
	/** Ruta en disco de la biblioteca: de ella salen las subcarpetas y el espacio libre. */
	libraryPath: string;
	/** Carpeta dentro de la biblioteca donde caen los ficheros. */
	initialSubpath?: string;
	onUploaded?: () => void;
}

function DiskGauge({ disk }: Readonly<{ disk: DiskUsage }>) {
	const usedPct = disk.totalBytes > 0 ? ((disk.totalBytes - disk.freeBytes) / disk.totalBytes) * 100 : 0;
	const tight = disk.freeBytes < 5 * 1024 * 1024 * 1024;

	return (
		<Stack sx={{ minWidth: 190 }}>
			<Stack direction="row" sx={{ justifyContent: "space-between", mb: 0.5 }}>
				<Typography sx={{ fontFamily: "'JetBrains Mono', monospace", fontSize: "10px", color: "var(--ds-muted)" }}>
					DISCO
				</Typography>
				<Typography
					sx={{
						fontFamily: "'JetBrains Mono', monospace",
						fontSize: "10px",
						color: tight ? "var(--ds-warn)" : "var(--ds-muted)",
					}}
				>
					{formatBytes(disk.freeBytes, 1)} libres de {formatBytes(disk.totalBytes, 1)}
				</Typography>
			</Stack>
			<LinearProgress
				variant="determinate"
				value={usedPct}
				sx={{ height: 6, "& .MuiLinearProgress-bar": { backgroundColor: tight ? "var(--ds-warn)" : "var(--ds-red-glow)" } }}
			/>
		</Stack>
	);
}

export default function UploadPanel({ libraryId, libraryPath, initialSubpath, onUploaded }: Readonly<Props>) {
	const fileInputRef = useRef<HTMLInputElement>(null);

	const [subpath, setSubpath] = useState(initialSubpath ?? "");
	const [items, setItems] = useState<UploadQueueItem[]>([]);
	const [isDragging, setIsDragging] = useState(false);

	const [subfolders, setSubfolders] = useState<FolderEntry[]>([]);
	const [disk, setDisk] = useState<DiskUsage | null>(null);
	const [newFolder, setNewFolder] = useState("");
	const [creatingFolder, setCreatingFolder] = useState(false);

	const [error, setError] = useState<string | null>(null);
	const [successCount, setSuccessCount] = useState<number | null>(null);


	const loadDestination = useCallback((path: string, alive: () => boolean = () => true) => {
		return Promise.allSettled([filesystemApi.browse(path), filesystemApi.disk(path)]).then(([listing, usage]) => {
			if (!alive()) return;
			setSubfolders(listing.status === "fulfilled" ? listing.value.entries : []);
			setDisk(usage.status === "fulfilled" ? usage.value : null);
		});
	}, []);

	useEffect(() => {
		if (!libraryPath) return;

		let cancelled = false;
		void loadDestination(libraryPath, () => !cancelled);

		return () => {
			cancelled = true;
		};
	}, [libraryPath, loadDestination]);

	const createQueueItem = useCallback(
		(file: File): UploadQueueItem => {
			const itemId = `${file.name}-${file.size}-${Date.now()}-${Math.random()}`;

			const tusUploadInstance = new TusUpload({
				file,
				libraryId,
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
					onUploaded?.();
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
		[libraryId, subpath, onUploaded]
	);

	const handleAddFiles = (files: File[]) => {
		setError(null);
		const newItems = files.map((f) => createQueueItem(f));
		setItems((prev) => [...prev, ...newItems]);
		for (const item of newItems) {
			void item.tusUpload.start();
		}
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

	const dropBorderColor = isDragging ? "var(--ds-red-glow)" : "var(--ds-border)";
	const dropBgColor = isDragging ? "rgba(var(--ds-red-rgb), 0.15)" : "var(--ds-bg-sunken)";

	const hasUploading = items.some((it) => it.status === "uploading");
	const hasPausedOrIdle = items.some(
		(it) => it.status === "idle" || it.status === "paused" || it.status === "error"
	);

	return (
		<Box>
			<Stack spacing={3}>

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

				<Card sx={{ overflow: "hidden" }}>
					<HudFrame />
					<CardContent>
						<Typography variant="h6" sx={{ fontWeight: 600, mb: 2 }}>
							Destino
						</Typography>

						<Stack direction={{ xs: "column", sm: "row" }} spacing={2} sx={{ alignItems: "center" }}>
								<FormControl size="small" sx={{ minWidth: 220, flexGrow: 1 }}>
									<InputLabel id="subfolder-select-label">Carpeta destino</InputLabel>
									<Select
										labelId="subfolder-select-label"
										label="Carpeta destino"
										value={subpath}
										onChange={(e) => setSubpath(e.target.value)}
										disabled={hasUploading || !libraryPath}
									>
										<MenuItem value="">
											<em>Raíz de la biblioteca</em>
										</MenuItem>
										{subfolders.map((folder) => (
											<MenuItem key={folder.path} value={folder.name}>
												{folder.name}
											</MenuItem>
										))}
									</Select>
								</FormControl>
						</Stack>

						<Stack direction={{ xs: "column", sm: "row" }} spacing={2} sx={{ alignItems: "center", mt: 2 }}>
							<TextField
								label="Crear carpeta nueva aquí"
								size="small"
								value={newFolder}
								onChange={(e) => setNewFolder(e.target.value)}
								disabled={creatingFolder || !libraryPath}
								placeholder="ej. Segunda temporada"
								sx={{ flexGrow: 1 }}
							/>
							<Button
								variant="outlined"
								startIcon={<CreateNewFolderIcon />}
								disabled={creatingFolder || !newFolder.trim() || !libraryPath}
								onClick={() => {
									setCreatingFolder(true);
									filesystemApi
										.createFolder(libraryPath, newFolder.trim())
										.then(async (created) => {
											setNewFolder("");
											setSubpath(created.name);
											await loadDestination(libraryPath);
										})
										.catch((err: unknown) => {
											setError(err instanceof Error ? err.message : "No se pudo crear la carpeta.");
										})
										.finally(() => {
											setCreatingFolder(false);
										});
								}}
							>
								Crear
							</Button>

							{disk?.available && <DiskGauge disk={disk} />}
						</Stack>
					</CardContent>
				</Card>

				{/* Zona Drag and Drop */}
				<Card sx={{ overflow: "hidden" }}>
					<HudFrame />
					<CardContent>
						<Typography variant="h6" sx={{ fontWeight: 600, mb: 2 }}>
							Ficheros
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
									borderColor: "var(--ds-red-glow)",
									backgroundColor: "rgba(var(--ds-red-rgb), 0.08)",
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
