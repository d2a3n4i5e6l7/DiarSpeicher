import {
	Alert,
	Box,
	Button,
	Card,
	Chip,
	CircularProgress,
	Dialog,
	DialogActions,
	DialogContent,
	DialogTitle,
	FormControlLabel,
	IconButton,
	Stack,
	Switch,
	Table,
	TableBody,
	TableCell,
	TableContainer,
	TableHead,
	TableRow,
	TextField,
	Tooltip,
	Typography,
} from "@mui/material";
import AddIcon from "@mui/icons-material/Add";
import EditIcon from "@mui/icons-material/Edit";
import DeleteIcon from "@mui/icons-material/Delete";
import RefreshIcon from "@mui/icons-material/Refresh";
import SecurityIcon from "@mui/icons-material/Security";
import { useCallback, useEffect, useState } from "react";
import PageHeader from "../components/PageHeader";
import HudFrame from "../components/HudFrame";
import { DS } from "../theme";
import { rolesApi, type RoleItem } from "../api/endpoints";

export default function RolesPage() {
	const [roles, setRoles] = useState<RoleItem[]>([]);
	const [loading, setLoading] = useState(true);
	const [error, setError] = useState<string | null>(null);
	const [successMsg, setSuccessMsg] = useState<string | null>(null);

	// Create Dialog
	const [openCreate, setOpenCreate] = useState(false);
	const [newName, setNewName] = useState("");
	const [newDescription, setNewDescription] = useState("");
	const [newIsAdmin, setNewIsAdmin] = useState(false);
	const [savingRole, setSavingRole] = useState(false);

	// Edit Dialog
	const [openEdit, setOpenEdit] = useState(false);
	const [editingRole, setEditingRole] = useState<RoleItem | null>(null);
	const [editName, setEditName] = useState("");
	const [editDescription, setEditDescription] = useState("");
	const [editIsAdmin, setEditIsAdmin] = useState(false);

	// Delete Dialog
	const [deleteRole, setDeleteRole] = useState<RoleItem | null>(null);

	const loadRoles = useCallback(async () => {
		setLoading(true);
		setError(null);
		try {
			const list = await rolesApi.list();
			setRoles(list || []);
		} catch (err: unknown) {
			setError(err instanceof Error ? err.message : "Error cargando la lista de roles.");
		} finally {
			setLoading(false);
		}
	}, []);

	useEffect(() => {
		let isMounted = true;
		const init = async () => {
			try {
				const list = await rolesApi.list();
				if (isMounted) {
					setRoles(list || []);
				}
			} catch (err: unknown) {
				if (isMounted) {
					setError(err instanceof Error ? err.message : "Error cargando la lista de roles.");
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

	const handleCreateRole = async (e: React.SyntheticEvent) => {
		e.preventDefault();
		if (!newName.trim()) {
			setError("El nombre del rol es obligatorio.");
			return;
		}

		setSavingRole(true);
		setError(null);
		try {
			await rolesApi.create({
				name: newName.trim(),
				description: newDescription.trim() || undefined,
				is_admin: newIsAdmin,
			});
			setSuccessMsg(`Rol "${newName}" creado con éxito.`);
			setOpenCreate(false);
			setNewName("");
			setNewDescription("");
			setNewIsAdmin(false);
			await loadRoles();
		} catch (err: unknown) {
			setError(err instanceof Error ? err.message : "Error creando el rol.");
		} finally {
			setSavingRole(false);
		}
	};

	const handleUpdateRole = async (e: React.SyntheticEvent) => {
		e.preventDefault();
		if (!editingRole || !editName.trim()) return;

		setSavingRole(true);
		setError(null);
		try {
			await rolesApi.update(editingRole.id, {
				name: editName.trim(),
				description: editDescription.trim() || undefined,
				is_admin: editIsAdmin,
			});
			setSuccessMsg(`Rol "${editName}" actualizado con éxito.`);
			setOpenEdit(false);
			await loadRoles();
		} catch (err: unknown) {
			setError(err instanceof Error ? err.message : "Error actualizando el rol.");
		} finally {
			setSavingRole(false);
		}
	};

	const handleDeleteRole = async () => {
		if (!deleteRole) return;
		setSavingRole(true);
		setError(null);
		try {
			await rolesApi.delete(deleteRole.id);
			setSuccessMsg(`Rol "${deleteRole.name}" eliminado.`);
			setDeleteRole(null);
			await loadRoles();
		} catch (err: unknown) {
			setError(err instanceof Error ? err.message : "Error eliminando el rol.");
		} finally {
			setSavingRole(false);
		}
	};

	let tableContent: React.ReactNode;
	if (loading) {
		tableContent = (
			<Box
				sx={{
					display: "flex",
					flexDirection: "column",
					alignItems: "center",
					justifyContent: "center",
					gap: 2,
					p: 6,
				}}
			>
				<CircularProgress size={28} thickness={5} />
				<Typography
					sx={{
						fontFamily: "'JetBrains Mono', monospace",
						fontSize: "11px",
						letterSpacing: "1px",
						color: DS.subtle,
					}}
				>
					CONSULTANDO MATRIZ DE PERMISOS...
				</Typography>
			</Box>
		);
	} else if (roles.length === 0) {
		tableContent = (
			<Box sx={{ p: 6, textAlign: "center" }}>
				<SecurityIcon sx={{ fontSize: 40, color: DS.borderRed, mb: 1.5 }} />
				<Typography
					sx={{
						fontFamily: "'Rajdhani', sans-serif",
						fontSize: "16px",
						fontWeight: 700,
						letterSpacing: "1.5px",
						textTransform: "uppercase",
						color: DS.platinum,
					}}
				>
					Matriz de permisos vacía
				</Typography>
				<Typography sx={{ fontFamily: "'Inter', sans-serif", fontSize: "13px", color: DS.muted, mt: 0.5 }}>
					Este nodo todavía no tiene ningún rol registrado.
				</Typography>
			</Box>
		);
	} else {
		tableContent = (
			<TableContainer>
				<Table>
					<TableHead>
						<TableRow>
							<TableCell sx={{ width: 96 }}>ID</TableCell>
							<TableCell>Designación</TableCell>
							<TableCell>Descripción</TableCell>
							<TableCell>Nivel</TableCell>
							<TableCell>Efectivos</TableCell>
							<TableCell align="right">Acciones</TableCell>
						</TableRow>
					</TableHead>
					<TableBody>
						{roles.map((r) => {
							const isAdmin = r.is_admin === true || (typeof r.is_admin === "number" && r.is_admin === 1);
							return (
								<TableRow key={r.id} hover>
									<TableCell>
										<Box component="span" className="ds-pill-mono">
											{String(r.id).padStart(3, "0")}
										</Box>
									</TableCell>
									<TableCell>
										<Typography
											sx={{
												fontFamily: "'Rajdhani', sans-serif",
												fontSize: "15px",
												fontWeight: 700,
												letterSpacing: "1px",
												textTransform: "uppercase",
												color: "#FFFFFF",
											}}
										>
											{r.name}
										</Typography>
									</TableCell>
									<TableCell>
										<Typography variant="body2" color="text.secondary">
											{r.description || "—"}
										</Typography>
									</TableCell>
									<TableCell>
										{isAdmin ? (
											<Chip
												icon={<SecurityIcon />}
												label="ADMINISTRADOR"
												size="small"
												color="primary"
											/>
										) : (
											<Chip label="ESTÁNDAR" size="small" variant="outlined" />
										)}
									</TableCell>
									<TableCell>
										<Chip
											label={`${String(r.user_count ?? 0).padStart(2, "0")} USUARIOS`}
											size="small"
											variant="outlined"
										/>
									</TableCell>
									<TableCell align="right">
										<Tooltip title="Editar Rol">
											<IconButton
												size="small"
												onClick={() => {
													setEditingRole(r);
													setEditName(r.name);
													setEditDescription(r.description || "");
													setEditIsAdmin(isAdmin);
													setOpenEdit(true);
												}}
											>
												<EditIcon fontSize="small" />
											</IconButton>
										</Tooltip>
										<Tooltip title="Eliminar Rol">
											<IconButton
												size="small"
												color="error"
												onClick={() => setDeleteRole(r)}
												disabled={isAdmin}
											>
												<DeleteIcon fontSize="small" />
											</IconButton>
										</Tooltip>
									</TableCell>
								</TableRow>
							);
						})}
					</TableBody>
				</Table>
			</TableContainer>
		);
	}

	return (
		<Box>
			<PageHeader
				title="Gestión de Roles"
				subtitle="Configura los niveles de acceso y permisos para los usuarios del sistema."
				actions={
					<>
						<Button
							startIcon={<RefreshIcon />}
							onClick={() => {
								void loadRoles();
							}}
							disabled={loading}
							sx={{
								color: "#A3ABB8",
								border: `1px solid ${DS.border}`,
								backgroundColor: DS.bgSunken,
								"&:hover": { borderColor: "#383E4C", backgroundColor: DS.bgSurface, color: "#FFFFFF" },
							}}
						>
							ACTUALIZAR
						</Button>
						<Button
							id="create-role-btn"
							variant="contained"
							startIcon={<AddIcon />}
							onClick={() => setOpenCreate(true)}
							className="btn-tactical"
							sx={{ background: DS.red, borderColor: DS.redGlow, color: "#FFFFFF" }}
						>
							NUEVO ROL
						</Button>
					</>
				}
			/>

			{error && (
				<Alert severity="error" sx={{ mb: 3 }} onClose={() => setError(null)}>
					{error}
				</Alert>
			)}

			{successMsg && (
				<Alert severity="success" sx={{ mb: 3 }} onClose={() => setSuccessMsg(null)}>
					{successMsg}
				</Alert>
			)}

			<Card sx={{ overflow: "hidden" }}>
				<HudFrame />
				{tableContent}
			</Card>

			{/* Modal: Crear Rol */}
			<Dialog open={openCreate} onClose={() => setOpenCreate(false)} maxWidth="xs" fullWidth>
				<HudFrame />
				<form
					onSubmit={(e) => {
						void handleCreateRole(e);
					}}
				>
					<DialogTitle>Alta de nuevo rol</DialogTitle>
					<DialogContent>
						<Stack spacing={2.5} sx={{ mt: 1 }}>
							<TextField
								id="new-role-name"
								label="Nombre del Rol"
								fullWidth
								required
								value={newName}
								onChange={(e) => setNewName(e.target.value)}
							/>
							<TextField
								id="new-role-description"
								label="Descripción"
								fullWidth
								multiline
								rows={2}
								value={newDescription}
								onChange={(e) => setNewDescription(e.target.value)}
							/>
							<FormControlLabel
								control={
									<Switch
										checked={newIsAdmin}
										onChange={(e) => setNewIsAdmin(e.target.checked)}
									/>
								}
								label="Privilegios de Administrador"
							/>
						</Stack>
					</DialogContent>
					<DialogActions>
						<Button onClick={() => setOpenCreate(false)} disabled={savingRole}>
							Cancelar
						</Button>
						<Button type="submit" variant="contained" disabled={savingRole}>
							{savingRole ? "Creando..." : "Crear Rol"}
						</Button>
					</DialogActions>
				</form>
			</Dialog>

			{/* Modal: Editar Rol */}
			<Dialog open={openEdit} onClose={() => setOpenEdit(false)} maxWidth="xs" fullWidth>
				<HudFrame />
				<form
					onSubmit={(e) => {
						void handleUpdateRole(e);
					}}
				>
					<DialogTitle>Editar Rol: {editingRole?.name}</DialogTitle>
					<DialogContent>
						<Stack spacing={2.5} sx={{ mt: 1 }}>
							<TextField
								label="Nombre del Rol"
								fullWidth
								required
								value={editName}
								onChange={(e) => setEditName(e.target.value)}
							/>
							<TextField
								label="Descripción"
								fullWidth
								multiline
								rows={2}
								value={editDescription}
								onChange={(e) => setEditDescription(e.target.value)}
							/>
							<FormControlLabel
								control={
									<Switch
										checked={editIsAdmin}
										onChange={(e) => setEditIsAdmin(e.target.checked)}
									/>
								}
								label="Privilegios de Administrador"
							/>
						</Stack>
					</DialogContent>
					<DialogActions>
						<Button onClick={() => setOpenEdit(false)} disabled={savingRole}>
							Cancelar
						</Button>
						<Button type="submit" variant="contained" disabled={savingRole}>
							{savingRole ? "Guardando..." : "Guardar Cambios"}
						</Button>
					</DialogActions>
				</form>
			</Dialog>

			{/* Modal: Confirmar Eliminación */}
			<Dialog open={Boolean(deleteRole)} onClose={() => setDeleteRole(null)} maxWidth="xs" fullWidth>
				<HudFrame />
				<DialogTitle
					sx={{ color: DS.redGlow, backgroundColor: "#160303", borderBottom: `1px solid ${DS.borderRed}` }}
				>
					Confirmar baja de rol
				</DialogTitle>
				<DialogContent>
					<Typography variant="body2">
						¿Estás seguro de que deseas eliminar el rol <strong>{deleteRole?.name}</strong>?
					</Typography>
				</DialogContent>
				<DialogActions>
					<Button onClick={() => setDeleteRole(null)} disabled={savingRole}>
						Cancelar
					</Button>
					<Button
						onClick={() => {
							void handleDeleteRole();
						}}
						color="error"
						variant="contained"
						disabled={savingRole}
					>
						{savingRole ? "Eliminando..." : "Eliminar"}
					</Button>
				</DialogActions>
			</Dialog>
		</Box>
	);
}
