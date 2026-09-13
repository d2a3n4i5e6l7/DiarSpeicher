import { useEffect, useState } from "react";
import {
	Alert,
	Box,
	Button,
	Chip,
	CircularProgress,
	Dialog,
	DialogActions,
	DialogContent,
	DialogTitle,
	IconButton,
	Stack,
	TextField,
	Tooltip,
	Typography,
} from "@mui/material";
import ArrowUpwardIcon from "@mui/icons-material/ArrowUpward";
import FolderIcon from "@mui/icons-material/Folder";
import FolderOpenIcon from "@mui/icons-material/FolderOpen";
import CreateNewFolderIcon from "@mui/icons-material/CreateNewFolder";
import LockIcon from "@mui/icons-material/Lock";
import SearchIcon from "@mui/icons-material/Search";
import RefreshIcon from "@mui/icons-material/Refresh";
import HudFrame from "./HudFrame";
import {
	filesystemApi,
	type FolderEntry,
	type FolderHit,
	type FolderListing,
	type FolderRoot,
} from "../api/endpoints";
import { DS } from "../theme";

interface Props {
	onClose: () => void;
	onSelect: (path: string) => void;
	/** Carpeta donde abrir. Si no cae dentro de una raiz, el backend arranca en la primera. */
	initialPath?: string;
}

/** Resultado atado a la carpeta que se pidio: mientras la clave no coincide, lo que hay en
 *  pantalla es de la carpeta anterior y eso es exactamente "cargando". Evita un `loading`
 *  aparte y con el la escritura de estado dentro del efecto. */
interface BrowseResult {
	key: string;
	listing: FolderListing | null;
	error: string | null;
}

/**
 * El padre lo monta solo cuando esta abierto, para que `initialPath` entre por el estado
 * inicial en vez de sincronizarse con un efecto.
 */
export default function FolderPickerDialog({ onClose, onSelect, initialPath }: Readonly<Props>) {
	const [roots, setRoots] = useState<FolderRoot[]>([]);
	const [requested, setRequested] = useState<string | undefined>(initialPath);
	const [result, setResult] = useState<BrowseResult | null>(null);
	const [query, setQuery] = useState("");
	const [found, setFound] = useState<{ key: string; hits: FolderHit[] } | null>(null);
	const [indexing, setIndexing] = useState(false);
	const [newFolderOpen, setNewFolderOpen] = useState(false);
	const [newFolderName, setNewFolderName] = useState("");
	const [creatingFolder, setCreatingFolder] = useState(false);
	const [folderCreateError, setFolderCreateError] = useState<string | null>(null);

	const requestKey = requested ?? "@root";

	useEffect(() => {
		let cancelled = false;
		filesystemApi.roots().then(
			(list) => {
				if (!cancelled) setRoots(list);
			},
			() => {
				if (!cancelled) setRoots([]);
			},
		);

		return () => {
			cancelled = true;
		};
	}, []);

	useEffect(() => {
		let cancelled = false;
		filesystemApi.browse(requested).then(
			(listing) => {
				if (!cancelled) setResult({ key: requestKey, listing, error: null });
			},
			(e: unknown) => {
				if (!cancelled) {
					setResult({
						key: requestKey,
						listing: null,
						error: e instanceof Error ? e.message : "No se pudo leer la carpeta.",
					});
				}
			},
		);

		return () => {
			cancelled = true;
		};
	}, [requestKey, requested]);

	// El servidor descarta menos de dos letras; no se sale a buscar por una.
	const needle = query.trim();
	const searching = needle.length >= 2;

	useEffect(() => {
		if (!searching) return;

		let cancelled = false;
		const timer = setTimeout(() => {
			filesystemApi.search(needle).then(
				(list) => {
					if (!cancelled) setFound({ key: needle, hits: list });
				},
				() => {
					if (!cancelled) setFound({ key: needle, hits: [] });
				},
			);
		}, 250);

		return () => {
			cancelled = true;
			clearTimeout(timer);
		};
	}, [needle, searching]);

	const hits = searching && found?.key === needle ? found.hits : null;

	const fresh = result?.key === requestKey ? result : null;
	const loading = fresh === null;
	const listing = fresh?.listing ?? null;
	const error = fresh?.error ?? null;
	const current = listing?.path ?? "";

	// Una raiz es el contenedor de las bibliotecas, no una biblioteca, y el backend rechaza
	// darla de alta. Se bloquea aqui para que el error no aparezca despues de rellenar el
	// formulario entero.
	const sameFolder = (a: string, b: string) =>
		(a.length > 1 ? a.replace(/\/+$/, "") : a) === (b.length > 1 ? b.replace(/\/+$/, "") : b);
	const isRoot = (path: string) => roots.some((root) => sameFolder(root.path, path));
	const currentIsRoot = current !== "" && isRoot(current);

	const choose = (path: string) => {
		if (isRoot(path)) return;
		onSelect(path);
		onClose();
	};

	const handleCreateFolder = async (e: React.SyntheticEvent) => {
		e.preventDefault();
		const trimmed = newFolderName.trim();
		if (!trimmed || !current) return;

		setCreatingFolder(true);
		setFolderCreateError(null);
		try {
			const entry = await filesystemApi.createFolder(current, trimmed);
			setNewFolderOpen(false);
			setNewFolderName("");
			setRequested(entry.path);
		} catch (err: unknown) {
			setFolderCreateError(err instanceof Error ? err.message : "Error al crear la carpeta.");
		} finally {
			setCreatingFolder(false);
		}
	};

	return (
		<Dialog open onClose={onClose} maxWidth="md" fullWidth>
			<HudFrame />

			<DialogTitle>// Elegir carpeta</DialogTitle>

			<DialogContent sx={{ p: 3 }}>
				{/* Las raices son los volumenes del compose: el atajo para saltar de disco
				    sin escribir la ruta a mano. */}
				<Stack direction="row" spacing={1} sx={{ flexWrap: "wrap", gap: 1, mb: 2 }}>
					{roots.map((root) => (
						<Tooltip key={root.path} title={root.mounted ? root.path : `${root.path} · no montado`}>
							<span>
								<Chip
									label={root.name}
									size="small"
									disabled={!root.mounted}
									onClick={() => {
										setRequested(root.path);
									}}
									variant={current.startsWith(root.path) ? "filled" : "outlined"}
								/>
							</span>
						</Tooltip>
					))}
				</Stack>

				{error && (
					<Alert severity="error" sx={{ mb: 2 }}>
						{error}
					</Alert>
				)}

				<Stack direction="row" spacing={1} sx={{ alignItems: "center", mb: 1.5 }}>
					<TextField
						fullWidth
						size="small"
						value={query}
						onChange={(e) => {
							setQuery(e.target.value);
						}}
						placeholder="Buscar carpeta por nombre..."
						slotProps={{ input: { startAdornment: <SearchIcon fontSize="small" sx={{ mr: 1, color: DS.muted }} /> } }}
					/>
					<Tooltip title="Reconstruir el índice de nombres">
						<span>
							<IconButton
								size="small"
								disabled={indexing}
								onClick={() => {
									setIndexing(true);
									void filesystemApi.rebuildIndex().finally(() => {
										setIndexing(false);
									});
								}}
								sx={{ color: DS.text2, border: `1px solid ${DS.border}`, borderRadius: "2px", p: 0.5 }}
							>
								<RefreshIcon fontSize="small" />
							</IconButton>
						</span>
					</Tooltip>
				</Stack>

				<Stack
					direction="row"
					spacing={1}
					sx={{
						alignItems: "center",
						mb: 1.5,
						p: 1,
						backgroundColor: DS.bgSunken,
						border: `1px solid ${DS.border}`,
					}}
				>
					<Tooltip title={listing?.parent ? "Subir un nivel" : "Ya estas en la carpeta raíz"}>
						<span>
							<IconButton
								size="small"
								disabled={!listing?.parent || loading}
								onClick={() => {
									setRequested(listing?.parent ?? undefined);
								}}
								sx={{ color: DS.text2, border: `1px solid ${DS.border}`, borderRadius: "2px", p: 0.5 }}
							>
								<ArrowUpwardIcon fontSize="small" />
							</IconButton>
						</span>
					</Tooltip>

					<Typography
						noWrap
						sx={{ fontFamily: "'JetBrains Mono', monospace", fontSize: "13px", color: DS.platinum, flex: 1 }}
					>
						{current || "—"}
					</Typography>

					<Button
						size="small"
						startIcon={<CreateNewFolderIcon fontSize="small" />}
						disabled={!current || loading}
						onClick={() => {
							setFolderCreateError(null);
							setNewFolderName("");
							setNewFolderOpen(true);
						}}
						sx={{
							fontFamily: "'Rajdhani', sans-serif",
							fontWeight: 700,
							fontSize: "12px",
							letterSpacing: "1px",
							color: DS.platinum,
							border: `1px solid ${DS.border}`,
							borderRadius: "2px",
							px: 1.5,
							py: 0.5,
							whiteSpace: "nowrap",
							"&:hover": { borderColor: DS.red, color: DS.redLight },
						}}
					>
						NUEVA CARPETA
					</Button>
				</Stack>

				<Box
					sx={{
						height: 360,
						overflowY: "auto",
						border: `1px solid ${DS.border}`,
						backgroundColor: DS.bgSunken,
					}}
				>
					{hits === null && loading && (
						<Stack sx={{ height: "100%", alignItems: "center", justifyContent: "center" }}>
							<CircularProgress size={26} thickness={5} />
						</Stack>
					)}

					{hits === null && !loading && listing?.entries.length === 0 && (
						<Stack sx={{ height: "100%", gap: 1.5, alignItems: "center", justifyContent: "center", p: 3 }}>
							<FolderOpenIcon sx={{ fontSize: 38, color: DS.borderRed }} />
							<Typography sx={{ fontSize: "13px", color: DS.muted, textAlign: "center" }}>
								Esta carpeta no tiene subcarpetas.
							</Typography>
							<Button
								size="small"
								variant="outlined"
								startIcon={<CreateNewFolderIcon fontSize="small" />}
								onClick={() => {
									setFolderCreateError(null);
									setNewFolderName("");
									setNewFolderOpen(true);
								}}
								sx={{
									fontFamily: "'Rajdhani', sans-serif",
									fontWeight: 700,
									fontSize: "13px",
									letterSpacing: "1.5px",
									color: DS.redGlow,
									borderColor: DS.borderRed,
									mt: 1,
									"&:hover": { borderColor: DS.redLight, backgroundColor: "rgba(var(--ds-red-rgb), 0.1)" },
								}}
							>
								CREAR CARPETA AQUÍ
							</Button>
						</Stack>
					)}

					{hits !== null &&
						hits.map((hit) => (
							<Stack
								key={hit.path}
								direction="row"
								spacing={1.5}
								sx={{
									alignItems: "center",
									px: 1.5,
									py: 1,
									borderBottom: `1px solid ${DS.borderSoft}`,
									"&:hover": { backgroundColor: "rgba(var(--ds-red-rgb), 0.08)" },
								}}
							>
								<FolderIcon fontSize="small" sx={{ color: DS.redGlow }} />
								<Box sx={{ flex: 1, minWidth: 0 }}>
									<Typography
										noWrap
										sx={{ fontFamily: "'Rajdhani', sans-serif", fontWeight: 600, fontSize: "14px", color: DS.platinum }}
									>
										{hit.name}
									</Typography>
									<Typography
										noWrap
										sx={{ fontFamily: "'JetBrains Mono', monospace", fontSize: "10px", color: DS.subtle }}
									>
										{hit.parent}
									</Typography>
								</Box>
								<Button
									size="small"
									onClick={() => {
										setQuery("");
										setRequested(hit.path);
									}}
									sx={{ minWidth: 0, px: 1.5 }}
								>
									Abrir
								</Button>
								<Button
									size="small"
									disabled={isRoot(hit.path)}
									onClick={() => choose(hit.path)}
									sx={{ minWidth: 0, px: 1.5 }}
								>
									Usar
								</Button>
							</Stack>
						))}

					{hits?.length === 0 && (
						<Stack sx={{ height: "100%", gap: 1, alignItems: "center", justifyContent: "center" }}>
							<Typography sx={{ fontSize: "12px", color: DS.muted }}>
								Sin coincidencias. Si es una carpeta nueva, reconstruye el índice.
							</Typography>
						</Stack>
					)}

					{hits === null &&
						!loading &&
						listing?.entries.map((entry) => (
							<FolderRow
								key={entry.path}
								entry={entry}
								onEnter={() => {
									setRequested(entry.path);
								}}
								onPick={() => choose(entry.path)}
							/>
						))}
				</Box>

				{listing?.truncated && (
					<Typography sx={{ fontFamily: "'JetBrains Mono', monospace", fontSize: "11px", color: DS.warn, mt: 1 }}>
						Hay más carpetas de las que caben en la lista. Entra en una para acotar.
					</Typography>
				)}
			</DialogContent>

			<DialogActions>
				<Button onClick={onClose} sx={{ color: DS.muted }}>
					Cancelar
				</Button>
				{currentIsRoot && (
					<Typography sx={{ fontFamily: "'JetBrains Mono', monospace", fontSize: "11px", color: DS.warn, mr: "auto" }}>
						Una raíz no puede ser una biblioteca. Entra o crea una carpeta dentro.
					</Typography>
				)}
				<Button
					variant="contained"
					disabled={!current || loading || currentIsRoot}
					onClick={() => choose(current)}
				>
					Usar esta carpeta
				</Button>
			</DialogActions>

			<Dialog
				open={newFolderOpen}
				onClose={() => {
					if (!creatingFolder) setNewFolderOpen(false);
				}}
				maxWidth="xs"
				fullWidth
			>
				<HudFrame />
				<DialogTitle>// Nueva carpeta</DialogTitle>
				<form onSubmit={(e) => { void handleCreateFolder(e); }}>
					<DialogContent sx={{ p: 3 }}>
						<Typography sx={{ fontFamily: "'JetBrains Mono', monospace", fontSize: "11px", color: DS.muted, mb: 2 }}>
							Ubicación: {current}
						</Typography>

						{folderCreateError && (
							<Alert severity="error" sx={{ mb: 2 }}>
								{folderCreateError}
							</Alert>
						)}

						<TextField
							autoFocus
							fullWidth
							size="small"
							label="Nombre de la carpeta"
							value={newFolderName}
							onChange={(e) => setNewFolderName(e.target.value)}
							placeholder="ej. Manga, Novelas, Comics"
							disabled={creatingFolder}
						/>
					</DialogContent>
					<DialogActions sx={{ p: 2 }}>
						<Button
							onClick={() => setNewFolderOpen(false)}
							disabled={creatingFolder}
							sx={{ color: DS.muted }}
						>
							Cancelar
						</Button>
						<Button
							type="submit"
							variant="contained"
							disabled={!newFolderName.trim() || creatingFolder}
							startIcon={creatingFolder ? <CircularProgress size={16} color="inherit" /> : <CreateNewFolderIcon />}
						>
							{creatingFolder ? "Creando..." : "Crear"}
						</Button>
					</DialogActions>
				</form>
			</Dialog>
		</Dialog>
	);
}

function FolderRow({
	entry,
	onEnter,
	onPick,
}: Readonly<{ entry: FolderEntry; onEnter: () => void; onPick: () => void }>) {
	return (
		<Stack
			direction="row"
			spacing={1.5}
			onDoubleClick={onPick}
			sx={{
				alignItems: "center",
				px: 1.5,
				py: 1,
				borderBottom: `1px solid ${DS.borderSoft}`,
				cursor: entry.readable ? "pointer" : "not-allowed",
				opacity: entry.readable ? 1 : 0.5,
				"&:hover": { backgroundColor: "rgba(var(--ds-red-rgb), 0.08)" },
			}}
		>
			<Box
				component="button"
				type="button"
				disabled={!entry.readable}
				onClick={onEnter}
				sx={{
					display: "flex",
					alignItems: "center",
					gap: 1.5,
					flex: 1,
					minWidth: 0,
					background: "none",
					border: "none",
					padding: 0,
					textAlign: "left",
					cursor: "inherit",
					color: "inherit",
					font: "inherit",
				}}
			>
				{entry.readable ? (
					<FolderIcon fontSize="small" sx={{ color: DS.redGlow }} />
				) : (
					<LockIcon fontSize="small" sx={{ color: DS.subtle }} />
				)}

				<Typography
					noWrap
					sx={{
						fontFamily: "'Rajdhani', sans-serif",
						fontWeight: 600,
						fontSize: "14px",
						color: DS.platinum,
						flex: 1,
						minWidth: 0,
					}}
				>
					{entry.name}
				</Typography>

				{/* Ficheros sueltos: la pista de que esta carpeta ya es una biblioteca. */}
				{entry.fileCount > 0 && (
					<Typography sx={{ fontFamily: "'JetBrains Mono', monospace", fontSize: "11px", color: DS.subtle }}>
						{entry.fileCount} fich.
					</Typography>
				)}
			</Box>

			<Button size="small" onClick={onPick} disabled={!entry.readable} sx={{ minWidth: 0, px: 1.5 }}>
				Usar
			</Button>
		</Stack>
	);
}
