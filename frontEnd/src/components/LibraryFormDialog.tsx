import {
	Alert,
	Box,
	Button,
	Chip,
	Dialog,
	DialogActions,
	DialogContent,
	DialogTitle,
	LinearProgress,
	Stack,
	TextField,
	Typography,
} from "@mui/material";
import CollectionsBookmarkIcon from "@mui/icons-material/CollectionsBookmark";
import FolderOpenIcon from "@mui/icons-material/FolderOpen";
import RefreshIcon from "@mui/icons-material/Refresh";
import React, { useEffect, useState } from "react";
import {
	filesystemApi,
	librariesApi,
	DEFAULT_LIBRARY_CONFIG,
	type LibraryConfig,
	type LibraryItem,
	type ScanPreview as ScanPreviewData,
} from "../api/endpoints";
import { errorMessage } from "../api/errorMessage";
import FolderPickerDialog from "./FolderPickerDialog";
import HudFrame from "./HudFrame";
import LibraryConfigForm from "./LibraryConfigForm";

export interface LibraryFormDialogProps {
	open: boolean;
	editing: LibraryItem | null;
	onClose: () => void;
	onSaved: (notice: string) => void;
}

export function ScanPreview({
	path,
	typedPath,
	pattern,
	onRun,
}: Readonly<{ path: string; typedPath: string; pattern: string; onRun: () => void }>) {
	const [preview, setPreview] = useState<{ key: string; data: ScanPreviewData | null; error: string | null } | null>(
		null
	);

	const key = `${path}#${pattern}`;

	useEffect(() => {
		if (!path) return;

		let cancelled = false;
		filesystemApi.preview(path, pattern).then(
			(data) => {
				if (!cancelled) setPreview({ key, data, error: null });
			},
			(e: unknown) => {
				if (!cancelled) {
					setPreview({
						key,
						data: null,
						error: e instanceof Error ? e.message : "No se pudo leer esa carpeta.",
					});
				}
			}
		);

		return () => {
			cancelled = true;
		};
	}, [key, path, pattern]);

	const fresh = preview?.key === key ? preview : null;
	const stale = typedPath.length > 0 && typedPath !== path;

	let previewContent: React.ReactNode = null;
	if (path && !stale && !fresh) {
		previewContent = <LinearProgress sx={{ height: 2 }} />;
	} else if (fresh?.error && !stale) {
		previewContent = (
			<Typography sx={{ fontFamily: "'JetBrains Mono', monospace", fontSize: "11px", color: "var(--ds-red-glow)" }}>
				{fresh.error}
			</Typography>
		);
	} else if (fresh?.data && !stale) {
		previewContent = (
			<React.Fragment>
				<Stack direction="row" spacing={1} sx={{ mb: 1.5, flexWrap: "wrap", gap: 1 }}>
					<Chip size="small" label={`${String(fresh.data.series.length)} series`} />
					<Chip size="small" label={`${String(fresh.data.totalVolumes)} tomos`} variant="outlined" />
				</Stack>

				{fresh.data.series.length === 0 && (
					<Typography sx={{ fontFamily: "'JetBrains Mono', monospace", fontSize: "11px", color: "var(--ds-subtle)" }}>
						Ninguna serie. No hay ficheros reconocibles ahí dentro.
					</Typography>
				)}

				<Box sx={{ maxHeight: 300, overflowY: "auto" }}>
					{fresh.data.series.map((serie) => (
						<Box
							key={serie.path}
							sx={{
								display: "flex",
								alignItems: "center",
								gap: 1,
								px: 1,
								py: 0.7,
								borderBottom: "1px solid var(--ds-border-soft)",
							}}
						>
							<CollectionsBookmarkIcon sx={{ fontSize: 15, color: "var(--ds-red-light)", flexShrink: 0 }} />
							<Box sx={{ minWidth: 0, flexGrow: 1 }}>
								<Typography
									noWrap
									sx={{ fontFamily: "'Rajdhani', sans-serif", fontSize: "13px", fontWeight: 600, color: "var(--ds-platinum)" }}
								>
									{serie.name}
								</Typography>
								{serie.isRoot && (
									<Typography sx={{ fontFamily: "'JetBrains Mono', monospace", fontSize: "10px", color: "var(--ds-subtle)" }}>
										tomos sueltos en la raíz
									</Typography>
								)}
							</Box>
							<Typography
								sx={{ fontFamily: "'JetBrains Mono', monospace", fontSize: "11px", color: "var(--ds-muted)", flexShrink: 0 }}
							>
								{serie.volumeCount}
							</Typography>
						</Box>
					))}
				</Box>
			</React.Fragment>
		);
	}

	return (
		<Box
			sx={{
				backgroundColor: "var(--ds-bg-sunken)",
				border: "1px solid var(--ds-border-head)",
				borderRadius: "3px",
				p: 2,
				mt: 1,
				minHeight: 260,
			}}
		>
			<Typography
				sx={{
					fontFamily: "'Rajdhani', sans-serif",
					fontWeight: 700,
					fontSize: "14px",
					letterSpacing: "1px",
					color: "var(--ds-text-strong)",
					textTransform: "uppercase",
					mb: 0.5,
				}}
			>
				Así quedaría
			</Typography>
			<Typography sx={{ fontSize: "11px", color: "var(--ds-muted)", mb: 2 }}>
				Ensayo sobre el disco. No se guarda nada hasta que pulses crear.
			</Typography>

			{(!path || stale) && (
				<Stack spacing={1.5} sx={{ alignItems: "flex-start" }}>
					<Typography sx={{ fontFamily: "'JetBrains Mono', monospace", fontSize: "11px", color: "var(--ds-subtle)" }}>
						{typedPath
							? "La ruta cambió. Recorrer el disco cuesta, así que se hace cuando lo pidas."
							: "Elige una ruta con Examinar, o escríbela y pulsa aquí."}
					</Typography>
					<Button variant="outlined" size="small" disabled={!typedPath} onClick={onRun} startIcon={<RefreshIcon />}>
						Calcular
					</Button>
				</Stack>
			)}

			{previewContent}
		</Box>
	);
}

function getSubmitLabel(saving: boolean, isEditing: boolean): string {
	if (saving) return "PROCESANDO...";
	if (isEditing) return "GUARDAR CAMBIOS";
	return "CREAR NODO";
}

export function LibraryFormContent({
	editing,
	onClose,
	onSaved,
}: Readonly<Omit<LibraryFormDialogProps, "open">>) {
	const [name, setName] = useState(editing?.name ?? "");
	const [path, setPath] = useState(editing?.path ?? "");
	const [description, setDescription] = useState(editing?.description ?? "");
	const emoji = editing?.emoji ?? "";
	const [config, setConfig] = useState<LibraryConfig>(editing?.config ?? DEFAULT_LIBRARY_CONFIG);
	const [formError, setFormError] = useState<string | null>(null);
	const [saving, setSaving] = useState(false);
	const [pickerOpen, setPickerOpen] = useState(false);
	const [previewPath, setPreviewPath] = useState("");

	const handleSubmit = async (e: React.SyntheticEvent) => {
		e.preventDefault();
		if (!name.trim() || !path.trim()) {
			setFormError("El nombre y la ruta de la biblioteca son obligatorios.");
			return;
		}

		setSaving(true);
		setFormError(null);
		try {
			if (editing) {
				await librariesApi.update(editing.id, {
					name: name.trim(),
					path: path.trim() === editing.path ? undefined : path.trim(),
					description: description.trim(),
					emoji: emoji.trim() || undefined,
					config,
				});
				onSaved(`Biblioteca "${name}" actualizada.`);
			} else {
				await librariesApi.create({
					name: name.trim(),
					path: path.trim(),
					description: description.trim(),
					emoji: emoji.trim() || undefined,
					config,
				});
				onSaved(`Biblioteca "${name}" dada de alta correctamente.`);
			}
			onClose();
		} catch (err: unknown) {
			setFormError(errorMessage(err, "Error guardando la biblioteca."));
		} finally {
			setSaving(false);
		}
	};

	const pathHelper = editing && path.trim() !== editing.path
		? "Mover la biblioteca reapunta el registro; los ficheros no se tocan."
		: "Dentro de una de las carpetas declaradas en el compose.";

	const dialogTitle = editing ? `// EDITAR BIBLIOTECA: ${editing.name}` : "// REGISTRAR NUEVA BIBLIOTECA DE ARCHIVO";

	return (
		<React.Fragment>
			<Dialog
				open
				onClose={() => {
					if (!saving) onClose();
				}}
				maxWidth="lg"
				fullWidth
				slotProps={{
					paper: {
						sx: {
							backgroundColor: "var(--ds-bg-overlay)",
							border: "1px solid var(--ds-red)",
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
							borderBottom: "1px solid var(--ds-border-head)",
							backgroundColor: "var(--ds-bg-sunken)",
							color: "var(--ds-text-strong)",
							py: 2,
						}}
					>
						{dialogTitle}
					</DialogTitle>

					<DialogContent
						sx={{
							p: 3,
							display: "grid",
							gap: 3,
							gridTemplateColumns: { xs: "1fr", md: "minmax(0, 1fr) minmax(0, 1fr)" },
							alignItems: "start",
						}}
					>
						<Stack spacing={2.5} sx={{ mt: 1 }}>
							{formError && (
								<Alert
									severity="error"
									onClose={() => {
										setFormError(null);
									}}
								>
									{formError}
								</Alert>
							)}

							<TextField
								label="Nombre de la biblioteca"
								value={name}
								onChange={(e) => setName(e.target.value)}
								required
								fullWidth
								disabled={saving}
								placeholder="Ej. Manga Seinen, Cómics DC, Novelas Ligeras"
							/>

							<Stack direction="row" spacing={1} sx={{ alignItems: "flex-start" }}>
								<TextField
									label="Ruta en el sistema de ficheros"
									value={path}
									onChange={(e) => setPath(e.target.value)}
									required
									fullWidth
									disabled={saving}
									helperText={pathHelper}
									placeholder="/libraries/manga"
									slotProps={{
										input: {
											sx: { fontFamily: "'JetBrains Mono', monospace", fontSize: "13px" },
										},
									}}
								/>
								<Button
									variant="outlined"
									disabled={saving}
									onClick={() => {
										setPickerOpen(true);
									}}
									startIcon={<FolderOpenIcon fontSize="small" />}
									sx={{ mt: 1, flexShrink: 0, whiteSpace: "nowrap" }}
								>
									Examinar
								</Button>
							</Stack>

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

							<Box
								sx={{
									backgroundColor: "var(--ds-bg-sunken)",
									border: "1px solid var(--ds-border-head)",
									borderRadius: "3px",
									p: 2,
								}}
							>
								<Typography
									sx={{
										fontFamily: "'Rajdhani', sans-serif",
										fontWeight: 700,
										fontSize: "14px",
										letterSpacing: "1px",
										color: "var(--ds-text-strong)",
										textTransform: "uppercase",
										mb: 2,
									}}
								>
									Escáner y lector
								</Typography>
								<LibraryConfigForm value={config} onChange={setConfig} disabled={saving} />
							</Box>
						</Stack>

						<ScanPreview
							path={previewPath}
							typedPath={path.trim()}
							pattern={config.libraryPattern}
							onRun={() => {
								setPreviewPath(path.trim());
							}}
						/>
					</DialogContent>

					<DialogActions
						sx={{
							p: 2.5,
							borderTop: "1px solid var(--ds-border-soft)",
							backgroundColor: "var(--ds-bg-sunken)",
							justifyContent: "space-between",
						}}
					>
						<Button
							onClick={onClose}
							disabled={saving}
							sx={{
								color: "var(--ds-muted)",
								fontFamily: "'Rajdhani', sans-serif",
								fontWeight: 700,
								"&:hover": { color: "var(--ds-text-strong)" },
							}}
						>
							CANCELAR
						</Button>
						<Button
							type="submit"
							variant="contained"
							disabled={saving}
							className="btn-tactical"
							sx={{
								background: "var(--ds-red)",
								borderColor: "var(--ds-red-glow)",
								color: "#FFFFFF",
								px: 3,
							}}
						>
							{getSubmitLabel(saving, Boolean(editing))}
						</Button>
					</DialogActions>
				</form>
			</Dialog>

			{pickerOpen && (
				<FolderPickerDialog
					initialPath={path.trim() || undefined}
					onClose={() => {
						setPickerOpen(false);
					}}
					onSelect={(selectedPath) => {
						setPath(selectedPath);
						setPreviewPath(selectedPath);
						setPickerOpen(false);
					}}
				/>
			)}
		</React.Fragment>
	);
}

export default function LibraryFormDialog({
	open,
	editing,
	onClose,
	onSaved,
}: Readonly<LibraryFormDialogProps>) {
	if (!open) return null;
	return (
		<LibraryFormContent
			key={editing?.id ?? "new"}
			editing={editing}
			onClose={onClose}
			onSaved={onSaved}
		/>
	);
}
