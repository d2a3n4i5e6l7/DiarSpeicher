import {
	Alert,
	Box,
	Button,
	Card,
	CardContent,
	Chip,
	CircularProgress,
	Dialog,
	DialogActions,
	DialogContent,
	DialogTitle,
	Divider,
	FormControl,
	IconButton,
	InputLabel,
	LinearProgress,
	List,
	ListItem,
	ListItemText,
	MenuItem,
	Select,
	Stack,
	TextField,
	Typography,
	useTheme,
} from "@mui/material";
import CloudUploadIcon from "@mui/icons-material/CloudUpload";
import AddIcon from "@mui/icons-material/Add";
import DeleteOutlinedIcon from "@mui/icons-material/DeleteOutlined";
import CheckCircleIcon from "@mui/icons-material/CheckCircle";
import FolderIcon from "@mui/icons-material/Folder";
import InsertDriveFileIcon from "@mui/icons-material/InsertDriveFile";
import React, { useCallback, useEffect, useRef, useState } from "react";
import {
	librariesApi,
	type LibraryItem,
	type UploadResponse,
} from "../api/endpoints";

function formatBytes(bytes: number, decimals = 2): string {
	if (bytes === 0) return "0 Bytes";
	const k = 1024;
	const dm = Math.max(0, decimals);
	const sizes = ["Bytes", "KB", "MB", "GB"];
	const i = Math.floor(Math.log(bytes) / Math.log(k));
	return `${Number.parseFloat((bytes / Math.pow(k, i)).toFixed(dm))} ${sizes[i]}`;
}

export default function UploadPage() {
	const theme = useTheme();
	const fileInputRef = useRef<HTMLInputElement>(null);

	const [libraries, setLibraries] = useState<LibraryItem[]>([]);
	const [selectedLibraryId, setSelectedLibraryId] = useState<string>("");
	const [subpath, setSubpath] = useState("");
	const [selectedFiles, setSelectedFiles] = useState<File[]>([]);
	const [isDragging, setIsDragging] = useState(false);

	const [loadingLibraries, setLoadingLibraries] = useState(true);
	const [uploading, setUploading] = useState(false);
	const [error, setError] = useState<string | null>(null);
	const [uploadResult, setUploadResult] = useState<UploadResponse | null>(null);

	// Create Library Dialog
	const [openCreateLib, setOpenCreateLib] = useState(false);
	const [libName, setLibName] = useState("");
	const [libPath, setLibPath] = useState("");
	const [libDesc, setLibDesc] = useState("");
	const [savingLib, setSavingLib] = useState(false);

	const loadLibraries = useCallback(async () => {
		setLoadingLibraries(true);
		setError(null);
		try {
			const list = await librariesApi.list();
			setLibraries(list || []);
			if (list && list.length > 0) {
				setSelectedLibraryId((prev) => prev || list[0].id);
			}
		} catch (err: unknown) {
			setError(err instanceof Error ? err.message : "Error cargando las bibliotecas.");
		} finally {
			setLoadingLibraries(false);
		}
	}, []);

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

	const handleFileSelect = (e: React.ChangeEvent<HTMLInputElement>) => {
		if (e.target.files && e.target.files.length > 0) {
			const filesArr = Array.from(e.target.files);
			setSelectedFiles((prev) => [...prev, ...filesArr]);
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
			const filesArr = Array.from(e.dataTransfer.files);
			setSelectedFiles((prev) => [...prev, ...filesArr]);
		}
	};

	const handleRemoveFile = (index: number) => {
		setSelectedFiles((prev) => prev.filter((_, i) => i !== index));
	};

	const handleUpload = async () => {
		if (!selectedLibraryId) {
			setError("Selecciona una biblioteca de destino.");
			return;
		}
		if (selectedFiles.length === 0) {
			setError("Selecciona al menos un fichero para subir.");
			return;
		}

		setUploading(true);
		setError(null);
		setUploadResult(null);

		try {
			const result = await librariesApi.upload(
				selectedLibraryId,
				selectedFiles,
				subpath.trim() || undefined
			);
			setUploadResult(result);
			setSelectedFiles([]);
			if (fileInputRef.current) {
				fileInputRef.current.value = "";
			}
		} catch (err: unknown) {
			setError(err instanceof Error ? err.message : "Error durante la subida de ficheros.");
		} finally {
			setUploading(false);
		}
	};

	const handleCreateLibrary = async (e: React.SyntheticEvent) => {
		e.preventDefault();
		if (!libName.trim() || !libPath.trim()) {
			setError("El nombre y la ruta de la biblioteca son obligatorios.");
			return;
		}

		setSavingLib(true);
		setError(null);
		try {
			const created = await librariesApi.create({
				name: libName.trim(),
				path: libPath.trim(),
				description: libDesc.trim() || undefined,
			});
			setOpenCreateLib(false);
			setLibName("");
			setLibPath("");
			setLibDesc("");
			await loadLibraries();
			if (created?.id) {
				setSelectedLibraryId(created.id);
			}
		} catch (err: unknown) {
			setError(err instanceof Error ? err.message : "Error creando la biblioteca.");
		} finally {
			setSavingLib(false);
		}
	};

	const isDarkMode = theme.palette.mode === "dark";
	const defaultBorderColor = isDarkMode ? "rgba(255, 255, 255, 0.2)" : "rgba(0, 0, 0, 0.2)";
	const dropBorderColor = isDragging ? theme.palette.primary.main : defaultBorderColor;

	const draggingBgColor = isDarkMode ? "rgba(99, 102, 241, 0.1)" : "rgba(79, 70, 229, 0.05)";
	const dropBgColor = isDragging ? draggingBgColor : "transparent";

	return (
		<Box>
			<Stack direction="row" sx={{ justifyContent: "space-between", alignItems: "center", mb: 3 }}>
				<Box>
					<Typography variant="h5" component="h2" sx={{ fontWeight: 700 }}>
						Subida de Ficheros
					</Typography>
					<Typography variant="body2" color="text.secondary">
						Sube cómics, libros y documentos digitales directamente a las bibliotecas de DiarSpeicher.
					</Typography>
				</Box>
			</Stack>

			{error && (
				<Alert severity="error" sx={{ mb: 3 }} onClose={() => setError(null)}>
					{error}
				</Alert>
			)}

			<Stack spacing={3}>
				{/* Configuración de Biblioteca Destino */}
				<Card>
					<CardContent>
						<Typography variant="h6" sx={{ fontWeight: 600, mb: 2 }}>
							1. Seleccionar Biblioteca Destino
						</Typography>

						{loadingLibraries ? (
							<CircularProgress size={24} />
						) : (
							<Stack direction={{ xs: "column", sm: "row" }} spacing={2} sx={{ alignItems: "center" }}>
								<FormControl fullWidth size="small" sx={{ maxWidth: 400 }}>
									<InputLabel id="library-select-label">Biblioteca</InputLabel>
									<Select
										labelId="library-select-label"
										label="Biblioteca"
										value={selectedLibraryId}
										onChange={(e) => setSelectedLibraryId(e.target.value)}
										disabled={uploading}
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
									startIcon={<AddIcon />}
									onClick={() => setOpenCreateLib(true)}
									disabled={uploading}
								>
									Nueva Biblioteca
								</Button>

								<TextField
									label="Subcarpeta (opcional)"
									size="small"
									value={subpath}
									onChange={(e) => setSubpath(e.target.value)}
									disabled={uploading}
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
								Formatos compatibles: CBZ, CBR, EPUB, PDF, ZIP
							</Typography>
						</Box>

						{/* Lista de Ficheros Preparados */}
						{selectedFiles.length > 0 && (
							<Box sx={{ mt: 3 }}>
								<Stack direction="row" sx={{ justifyContent: "space-between", alignItems: "center", mb: 1 }}>
									<Typography variant="subtitle2" sx={{ fontWeight: 600 }}>
										Ficheros seleccionados ({selectedFiles.length})
									</Typography>
									<Button
										size="small"
										color="error"
										onClick={() => setSelectedFiles([])}
										disabled={uploading}
									>
										Limpiar todos
									</Button>
								</Stack>

								<List dense sx={{ maxHeight: 220, overflowY: "auto", border: 1, borderColor: "divider", borderRadius: 1 }}>
									{selectedFiles.map((file, idx) => (
										<ListItem
											key={`${file.name}-${file.size}-${idx}`}
											secondaryAction={
												<IconButton
													edge="end"
													size="small"
													onClick={() => handleRemoveFile(idx)}
													disabled={uploading}
												>
													<DeleteOutlinedIcon fontSize="small" />
												</IconButton>
											}
										>
											<InsertDriveFileIcon sx={{ mr: 1.5, color: "text.secondary" }} fontSize="small" />
											<ListItemText
												primary={file.name}
												secondary={formatBytes(file.size)}
											/>
										</ListItem>
									))}
								</List>

								{uploading && (
									<Box sx={{ mt: 2 }}>
										<LinearProgress />
										<Typography variant="caption" color="text.secondary" sx={{ mt: 0.5, display: "block" }}>
											Subiendo ficheros e indexando en DiarSpeicher...
										</Typography>
									</Box>
								)}

								<Button
									id="start-upload-btn"
									variant="contained"
									size="large"
									startIcon={uploading ? <CircularProgress size={20} color="inherit" /> : <CloudUploadIcon />}
									onClick={() => {
										void handleUpload();
									}}
									disabled={uploading || selectedFiles.length === 0}
									sx={{ mt: 2 }}
								>
									{uploading ? "Subiendo..." : "Iniciar Subida"}
								</Button>
							</Box>
						)}
					</CardContent>
				</Card>

				{/* Resultado de la Subida */}
				{uploadResult && (
					<Card sx={{ borderLeft: 4, borderColor: "success.main" }}>
						<CardContent>
							<Stack direction="row" spacing={1.5} sx={{ alignItems: "center", mb: 2 }}>
								<CheckCircleIcon color="success" />
								<Typography variant="h6" sx={{ fontWeight: 600 }}>
									Subida Completada con Éxito
								</Typography>
							</Stack>

							<Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
								Se procesaron correctamente <strong>{uploadResult.uploadedCount}</strong> ficheros.
								{uploadResult.scanJobTriggered && (
									<Chip
										label="Escaneo automático encolado"
										size="small"
										color="success"
										sx={{ ml: 1.5 }}
									/>
								)}
							</Typography>

							<Divider sx={{ my: 1.5 }} />

							<Typography variant="subtitle2" sx={{ fontWeight: 600, mb: 1 }}>
								Archivos recibidos en el servidor:
							</Typography>
							<List dense>
								{uploadResult.files.map((f, i) => (
									<ListItem key={`${f.path}-${i}`}>
										<FolderIcon sx={{ mr: 1, color: "text.secondary" }} fontSize="small" />
										<ListItemText primary={f.name} secondary={`${f.path} · ${formatBytes(f.size)}`} />
									</ListItem>
								))}
							</List>
						</CardContent>
					</Card>
				)}
			</Stack>

			{/* Modal: Crear Biblioteca */}
			<Dialog open={openCreateLib} onClose={() => setOpenCreateLib(false)} maxWidth="xs" fullWidth>
				<form
					onSubmit={(e) => {
						void handleCreateLibrary(e);
					}}
				>
					<DialogTitle>Nueva Biblioteca en DiarSpeicher</DialogTitle>
					<DialogContent>
						<Stack spacing={2.5} sx={{ mt: 1 }}>
							<TextField
								label="Nombre de la Biblioteca"
								fullWidth
								required
								value={libName}
								onChange={(e) => setLibName(e.target.value)}
								placeholder="ej. Comics Marvel"
							/>
							<TextField
								label="Ruta en el Disco"
								fullWidth
								required
								value={libPath}
								onChange={(e) => setLibPath(e.target.value)}
								placeholder="ej. /data/comics"
							/>
							<TextField
								label="Descripción (opcional)"
								fullWidth
								multiline
								rows={2}
								value={libDesc}
								onChange={(e) => setLibDesc(e.target.value)}
							/>
						</Stack>
					</DialogContent>
					<DialogActions>
						<Button onClick={() => setOpenCreateLib(false)} disabled={savingLib}>
							Cancelar
						</Button>
						<Button type="submit" variant="contained" disabled={savingLib}>
							{savingLib ? "Guardando..." : "Crear"}
						</Button>
					</DialogActions>
				</form>
			</Dialog>
		</Box>
	);
}
