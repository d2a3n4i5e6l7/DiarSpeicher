import {
	Accordion,
	AccordionDetails,
	AccordionSummary,
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
import FolderOpenIcon from "@mui/icons-material/FolderOpen";
import React, { useCallback, useEffect, useRef, useState } from "react";
import PageHeader from "../components/PageHeader";
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

function statusColor(status: string | undefined): "success" | "info" | "warning" | "default" {
	switch ((status ?? "").toUpperCase()) {
		case "READY":
			return "success";
		case LIBRARY_STATUS_SCANNING:
			return "info";
		case "MISSING":
			return "warning";
		default:
			return "default";
	}
}

export default function LibrariesPage() {
	const [libraries, setLibraries] = useState<LibraryItem[]>([]);
	const [loading, setLoading] = useState(true);
	const [error, setError] = useState<string | null>(null);
	const [notice, setNotice] = useState<string | null>(null);
	const [scanningIds, setScanningIds] = useState<string[]>([]);

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

	/**
	 * El backend publica el avance del escaneo por suscripcion GraphQL y esta SPA no tiene
	 * cliente GraphQL, asi que el estado se refresca sondeando mientras algo este escaneando.
	 */
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
					emoji: emoji.trim(),
					config,
				});
				setNotice(`Biblioteca ${name.trim()} actualizada.`);
			} else {
				await librariesApi.create({
					name: name.trim(),
					path: path.trim(),
					description: description.trim() || undefined,
					emoji: emoji.trim() || undefined,
					config,
				});
			}
			setFormOpen(false);
			await load();
		} catch (err: unknown) {
			setError(err instanceof Error ? err.message : "No se pudo guardar la biblioteca.");
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
			setNotice(`Biblioteca ${pendingDelete.name} eliminada del índice.`);
			setPendingDelete(null);
			await load();
		} catch (err: unknown) {
			setError(err instanceof Error ? err.message : "No se pudo eliminar la biblioteca.");
		} finally {
			setSaving(false);
		}
	};

	const handleScan = async (library: LibraryItem) => {
		setScanningIds((prev) => [...prev, library.id]);
		setError(null);
		try {
			await librariesApi.scan(library.id);
			setNotice(`Escaneo encolado para ${library.name}.`);
			await load(true);
		} catch (err: unknown) {
			setError(err instanceof Error ? err.message : "No se pudo encolar el escaneo.");
		} finally {
			setScanningIds((prev) => prev.filter((id) => id !== library.id));
		}
	};

	return (
		<Box sx={{ maxWidth: 1100, mx: "auto" }}>
			<PageHeader
				title="Bibliotecas"
				subtitle="Carpetas que DiarSpeicher indexa y sirve a los clientes de lectura."
				actions={
					<>
						<Button startIcon={<RefreshIcon />} onClick={() => void load()} disabled={loading}>
							Actualizar
						</Button>
						<Button variant="contained" startIcon={<AddIcon />} onClick={openForCreate}>
							Nueva biblioteca
						</Button>
					</>
				}
			/>

			{error && (
				<Alert severity="error" onClose={() => setError(null)} sx={{ mb: 2 }}>
					{error}
				</Alert>
			)}
			{notice && (
				<Alert severity="success" onClose={() => setNotice(null)} sx={{ mb: 2 }}>
					{notice}
				</Alert>
			)}

			{loading && (
				<Box sx={{ display: "flex", justifyContent: "center", py: 6 }}>
					<CircularProgress />
				</Box>
			)}

			{!loading && libraries.length === 0 && (
				<Card variant="outlined">
					<CardContent sx={{ textAlign: "center", py: 6 }}>
						<LibraryBooksIcon sx={{ fontSize: 48, color: "text.disabled", mb: 1 }} />
						<Typography variant="h6" sx={{ mb: 0.5 }}>
							Todavía no hay bibliotecas
						</Typography>
						<Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>
							Crea una apuntando a la carpeta donde guardas los libros.
						</Typography>
						<Button variant="contained" startIcon={<AddIcon />} onClick={openForCreate}>
							Nueva biblioteca
						</Button>
					</CardContent>
				</Card>
			)}

			{!loading && libraries.length > 0 && (
				<Box
					sx={{
						display: "grid",
						gap: 2,
						gridTemplateColumns: { xs: "1fr", sm: "repeat(2, 1fr)", lg: "repeat(3, 1fr)" },
					}}>
					{libraries.map((library) => {
						const scanning = isScanning(library);
						const busy = scanning || scanningIds.includes(library.id);
						return (
							<Card key={library.id} variant="outlined" sx={{ display: "flex", flexDirection: "column" }}>
								{busy && <LinearProgress />}
								<CardContent sx={{ flexGrow: 1 }}>
									<Stack direction="row" spacing={1} sx={{ alignItems: "flex-start", mb: 1 }}>
										<LibraryBooksIcon color="primary" />
										<Box sx={{ minWidth: 0, flexGrow: 1 }}>
											<Typography variant="subtitle1" noWrap sx={{ fontWeight: 600 }}>
												{library.name}
											</Typography>
											<Tooltip title={library.path}>
												<Typography
													variant="caption"
													color="text.secondary"
													sx={{ display: "flex", alignItems: "center", gap: 0.5, minWidth: 0 }}>
													<FolderOpenIcon sx={{ fontSize: 14 }} />
													<Box component="span" sx={{ overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
														{library.path}
													</Box>
												</Typography>
											</Tooltip>
										</Box>
										<Stack direction="row" spacing={0}>
											<Tooltip title={busy ? "Escaneo en curso" : "Escanear ahora"}>
												<span>
													<IconButton size="small" onClick={() => void handleScan(library)} disabled={busy}>
														<SyncIcon fontSize="small" />
													</IconButton>
												</span>
											</Tooltip>
											<Tooltip title="Editar">
												<IconButton size="small" onClick={() => openForEdit(library)}>
													<EditOutlinedIcon fontSize="small" />
												</IconButton>
											</Tooltip>
											<Tooltip title="Eliminar del índice">
												<IconButton size="small" color="error" onClick={() => setPendingDelete(library)}>
													<DeleteOutlinedIcon fontSize="small" />
												</IconButton>
											</Tooltip>
										</Stack>
									</Stack>

									<Stack direction="row" spacing={1} sx={{ flexWrap: "wrap", gap: 0.5, mb: 1 }}>
										<Chip size="small" label={library.status ?? "—"} color={statusColor(library.status)} variant="outlined" />
										<Chip size="small" label={`${library.seriesCount ?? 0} series`} variant="outlined" />
										<Chip size="small" label={`${library.mediaCount ?? 0} tomos`} variant="outlined" />
										{library.config && <Chip size="small" label={library.config.libraryType} variant="outlined" />}
									</Stack>

									{library.description && (
										<Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
											{library.description}
										</Typography>
									)}

									<Typography variant="caption" color="text.secondary">
										{library.lastScannedAt
											? `Último escaneo: ${new Date(library.lastScannedAt).toLocaleString()}`
											: "Nunca escaneada"}
									</Typography>
								</CardContent>
							</Card>
						);
					})}
				</Box>
			)}

			<Dialog open={formOpen} onClose={() => setFormOpen(false)} fullWidth maxWidth="sm">
				<form
					onSubmit={(e) => {
						void handleSubmit(e);
					}}>
					<DialogTitle>{editing ? `Editar ${editing.name}` : "Nueva biblioteca"}</DialogTitle>
					<DialogContent>
						<Stack spacing={2} sx={{ mt: 1 }}>
							<Stack direction="row" spacing={2}>
								<TextField
									label="Nombre"
									value={name}
									onChange={(e) => setName(e.target.value)}
									fullWidth
									required
									autoFocus
								/>
								<TextField
									label="Emoji"
									value={emoji}
									onChange={(e) => setEmoji(e.target.value)}
									sx={{ width: 110 }}
									slotProps={{ htmlInput: { maxLength: 4 } }}
								/>
							</Stack>
							<TextField
								label="Ruta en el servidor"
								value={path}
								onChange={(e) => setPath(e.target.value)}
								fullWidth
								required
								disabled={editing !== null}
								helperText={
									editing
										? "La ruta no se puede cambiar: el índice quedaría apuntando a ficheros que ya no existen."
										: "Ruta tal y como la ve el contenedor, por ejemplo /data/Mangas. Se crea si no existe."
								}
							/>
							<TextField
								label="Descripción"
								value={description}
								onChange={(e) => setDescription(e.target.value)}
								fullWidth
								multiline
								rows={2}
							/>

							<Accordion variant="outlined" disableGutters>
								<AccordionSummary expandIcon={<ExpandMoreIcon />}>
									<Typography variant="subtitle2">Configuración avanzada</Typography>
								</AccordionSummary>
								<AccordionDetails>
									<LibraryConfigForm value={config} onChange={setConfig} disabled={saving} />
								</AccordionDetails>
							</Accordion>
						</Stack>
					</DialogContent>
					<DialogActions>
						<Button onClick={() => setFormOpen(false)}>Cancelar</Button>
						<Button type="submit" variant="contained" disabled={saving}>
							{saving ? "Guardando…" : editing ? "Guardar" : "Crear"}
						</Button>
					</DialogActions>
				</form>
			</Dialog>

			<Dialog open={pendingDelete !== null} onClose={() => setPendingDelete(null)} maxWidth="xs" fullWidth>
				<DialogTitle>Eliminar biblioteca</DialogTitle>
				<DialogContent>
					<Typography variant="body2">
						Se quitará <strong>{pendingDelete?.name}</strong> del índice, junto con sus series y el progreso de
						lectura asociado.
					</Typography>
					<Alert severity="info" sx={{ mt: 2 }}>
						Los ficheros de <code>{pendingDelete?.path}</code> no se tocan.
					</Alert>
				</DialogContent>
				<DialogActions>
					<Button onClick={() => setPendingDelete(null)}>Cancelar</Button>
					<Button
						color="error"
						variant="contained"
						disabled={saving}
						onClick={() => {
							void handleDelete();
						}}>
						{saving ? "Eliminando…" : "Eliminar"}
					</Button>
				</DialogActions>
			</Dialog>

		</Box>
	);
}
