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
	FormControl,
	FormControlLabel,
	IconButton,
	InputLabel,
	MenuItem,
	Select,
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
import VpnKeyIcon from "@mui/icons-material/VpnKey";
import RefreshIcon from "@mui/icons-material/Refresh";
import { useCallback, useEffect, useState } from "react";
import { usersApi, rolesApi, type UserItem, type RoleItem } from "../api/endpoints";

export default function UsersPage() {
	const [users, setUsers] = useState<UserItem[]>([]);
	const [roles, setRoles] = useState<RoleItem[]>([]);
	const [loading, setLoading] = useState(true);
	const [error, setError] = useState<string | null>(null);
	const [successMsg, setSuccessMsg] = useState<string | null>(null);

	// Create User Dialog
	const [openCreate, setOpenCreate] = useState(false);
	const [newUsername, setNewUsername] = useState("");
	const [newPassword, setNewPassword] = useState("");
	const [newRoleId, setNewRoleId] = useState<number | "">("");
	const [savingUser, setSavingUser] = useState(false);

	// Edit User Dialog
	const [openEdit, setOpenEdit] = useState(false);
	const [editingUser, setEditingUser] = useState<UserItem | null>(null);
	const [editRoleId, setEditRoleId] = useState<number | "">("");
	const [editIsEnabled, setEditIsEnabled] = useState(true);

	// Password Dialog
	const [openPassword, setOpenPassword] = useState(false);
	const [passwordUser, setPasswordUser] = useState<UserItem | null>(null);
	const [newPasswordVal, setNewPasswordVal] = useState("");

	// Delete Dialog
	const [deleteUser, setDeleteUser] = useState<UserItem | null>(null);

	const loadData = useCallback(async () => {
		setLoading(true);
		setError(null);
		try {
			const [userList, roleList] = await Promise.all([usersApi.list(), rolesApi.list()]);
			setUsers(userList || []);
			setRoles(roleList || []);
		} catch (err: unknown) {
			setError(err instanceof Error ? err.message : "Error cargando usuarios o roles.");
		} finally {
			setLoading(false);
		}
	}, []);

	useEffect(() => {
		let isMounted = true;
		const init = async () => {
			try {
				const [userList, roleList] = await Promise.all([usersApi.list(), rolesApi.list()]);
				if (isMounted) {
					setUsers(userList || []);
					setRoles(roleList || []);
				}
			} catch (err: unknown) {
				if (isMounted) {
					setError(err instanceof Error ? err.message : "Error cargando usuarios o roles.");
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

	const handleCreateUser = async (e: React.SyntheticEvent) => {
		e.preventDefault();
		if (!newUsername.trim() || !newPassword) {
			setError("El usuario y la contraseña son obligatorios.");
			return;
		}

		setSavingUser(true);
		setError(null);
		try {
			const selectedRole = roles.find((r) => r.id === newRoleId)?.name ?? "reader";
			await usersApi.create({
				username: newUsername.trim(),
				password: newPassword,
				role: selectedRole,
				role_id: newRoleId === "" ? undefined : Number(newRoleId),
			});
			setSuccessMsg(`Usuario "${newUsername}" creado con éxito.`);
			setOpenCreate(false);
			setNewUsername("");
			setNewPassword("");
			setNewRoleId("");
			await loadData();
		} catch (err: unknown) {
			setError(err instanceof Error ? err.message : "Error creando el usuario.");
		} finally {
			setSavingUser(false);
		}
	};

	const handleUpdateUser = async (e: React.SyntheticEvent) => {
		e.preventDefault();
		if (!editingUser) return;

		setSavingUser(true);
		setError(null);
		try {
			const selectedRole = roles.find((r) => r.id === editRoleId)?.name;
			await usersApi.update(editingUser.id, {
				role: selectedRole,
				role_id: editRoleId === "" ? undefined : Number(editRoleId),
				is_enabled: editIsEnabled ? 1 : 0,
			});
			setSuccessMsg(`Usuario "${editingUser.username}" actualizado.`);
			setOpenEdit(false);
			await loadData();
		} catch (err: unknown) {
			setError(err instanceof Error ? err.message : "Error actualizando el usuario.");
		} finally {
			setSavingUser(false);
		}
	};

	const handleChangePassword = async (e: React.SyntheticEvent) => {
		e.preventDefault();
		if (!passwordUser || !newPasswordVal) return;

		setSavingUser(true);
		setError(null);
		try {
			await usersApi.changePassword(passwordUser.id, newPasswordVal);
			setSuccessMsg(`Contraseña de "${passwordUser.username}" cambiada con éxito.`);
			setOpenPassword(false);
			setNewPasswordVal("");
		} catch (err: unknown) {
			setError(err instanceof Error ? err.message : "Error cambiando la contraseña.");
		} finally {
			setSavingUser(false);
		}
	};

	const handleDeleteUser = async () => {
		if (!deleteUser) return;
		setSavingUser(true);
		setError(null);
		try {
			await usersApi.delete(deleteUser.id);
			setSuccessMsg(`Usuario "${deleteUser.username}" eliminado.`);
			setDeleteUser(null);
			await loadData();
		} catch (err: unknown) {
			setError(err instanceof Error ? err.message : "Error eliminando el usuario.");
		} finally {
			setSavingUser(false);
		}
	};

	let tableContent: React.ReactNode;
	if (loading) {
		tableContent = (
			<Box sx={{ display: "flex", justifyContent: "center", alignItems: "center", p: 6 }}>
				<CircularProgress />
			</Box>
		);
	} else if (users.length === 0) {
		tableContent = (
			<Box sx={{ p: 6, textAlign: "center" }}>
				<Typography color="text.secondary">No hay usuarios registrados.</Typography>
			</Box>
		);
	} else {
		tableContent = (
			<TableContainer>
				<Table>
					<TableHead>
						<TableRow>
							<TableCell>ID</TableCell>
							<TableCell>Usuario</TableCell>
							<TableCell>Rol</TableCell>
							<TableCell>Estado</TableCell>
							<TableCell align="right">Acciones</TableCell>
						</TableRow>
					</TableHead>
					<TableBody>
						{users.map((u) => {
							const isEnabled = u.is_enabled === true || u.is_enabled === 1;
							const isAdmin = u.is_admin === true || (typeof u.is_admin === "number" && u.is_admin === 1);
							return (
								<TableRow key={u.id} hover>
									<TableCell>{u.id}</TableCell>
									<TableCell>
										<Typography sx={{ fontWeight: 600 }}>{u.username}</Typography>
									</TableCell>
									<TableCell>
										<Stack direction="row" spacing={1} sx={{ alignItems: "center" }}>
											<Chip
												label={u.role || "Sin rol"}
												size="small"
												variant="outlined"
												color={isAdmin ? "primary" : "default"}
											/>
											{isAdmin && <Chip label="Admin" size="small" color="primary" />}
										</Stack>
									</TableCell>
									<TableCell>
										<Chip
											label={isEnabled ? "Activo" : "Inactivo"}
											size="small"
											color={isEnabled ? "success" : "default"}
											variant={isEnabled ? "filled" : "outlined"}
										/>
									</TableCell>
									<TableCell align="right">
										<Tooltip title="Cambiar Contraseña">
											<IconButton
												size="small"
												onClick={() => {
													setPasswordUser(u);
													setNewPasswordVal("");
													setOpenPassword(true);
												}}
											>
												<VpnKeyIcon fontSize="small" />
											</IconButton>
										</Tooltip>
										<Tooltip title="Editar Rol y Estado">
											<IconButton
												size="small"
												onClick={() => {
													setEditingUser(u);
													setEditRoleId(u.role_id ?? "");
													setEditIsEnabled(isEnabled);
													setOpenEdit(true);
												}}
											>
												<EditIcon fontSize="small" />
											</IconButton>
										</Tooltip>
										<Tooltip title="Eliminar Usuario">
											<IconButton
												size="small"
												color="error"
												onClick={() => setDeleteUser(u)}
												disabled={u.username === "admin"}
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
			<Stack direction="row" sx={{ justifyContent: "space-between", alignItems: "center", mb: 3 }}>
				<Box>
					<Typography variant="h5" component="h2" sx={{ fontWeight: 700 }}>
						Gestión de Usuarios
					</Typography>
					<Typography variant="body2" color="text.secondary">
						Administra las cuentas de usuario y sus credenciales de acceso.
					</Typography>
				</Box>

				<Stack direction="row" spacing={1.5}>
					<Button
						variant="outlined"
						startIcon={<RefreshIcon />}
						onClick={() => {
							void loadData();
						}}
						disabled={loading}
					>
						Refrescar
					</Button>
					<Button
						id="create-user-btn"
						variant="contained"
						startIcon={<AddIcon />}
						onClick={() => setOpenCreate(true)}
					>
						Nuevo Usuario
					</Button>
				</Stack>
			</Stack>

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

			<Card>
				{tableContent}
			</Card>

			{/* Modal: Crear Usuario */}
			<Dialog open={openCreate} onClose={() => setOpenCreate(false)} maxWidth="xs" fullWidth>
				<form
					onSubmit={(e) => {
						void handleCreateUser(e);
					}}
				>
					<DialogTitle>Crear Nuevo Usuario</DialogTitle>
					<DialogContent>
						<Stack spacing={2.5} sx={{ mt: 1 }}>
							<TextField
								id="new-user-username"
								label="Nombre de Usuario"
								fullWidth
								required
								value={newUsername}
								onChange={(e) => setNewUsername(e.target.value)}
							/>
							<TextField
								id="new-user-password"
								label="Contraseña"
								type="password"
								fullWidth
								required
								value={newPassword}
								onChange={(e) => setNewPassword(e.target.value)}
							/>
							<FormControl fullWidth size="small">
								<InputLabel id="new-user-role-label">Rol Asignado</InputLabel>
								<Select
									labelId="new-user-role-label"
									label="Rol Asignado"
									value={newRoleId}
									onChange={(e) => setNewRoleId(e.target.value)}
								>
									<MenuItem value="">
										<em>Sin rol específico</em>
									</MenuItem>
									{roles.map((r) => (
										<MenuItem key={r.id} value={r.id}>
											{r.name} {r.is_admin ? "(Admin)" : ""}
										</MenuItem>
									))}
								</Select>
							</FormControl>
						</Stack>
					</DialogContent>
					<DialogActions>
						<Button onClick={() => setOpenCreate(false)} disabled={savingUser}>
							Cancelar
						</Button>
						<Button type="submit" variant="contained" disabled={savingUser}>
							{savingUser ? "Creando..." : "Crear Usuario"}
						</Button>
					</DialogActions>
				</form>
			</Dialog>

			{/* Modal: Editar Usuario */}
			<Dialog open={openEdit} onClose={() => setOpenEdit(false)} maxWidth="xs" fullWidth>
				<form
					onSubmit={(e) => {
						void handleUpdateUser(e);
					}}
				>
					<DialogTitle>Editar Usuario: {editingUser?.username}</DialogTitle>
					<DialogContent>
						<Stack spacing={2.5} sx={{ mt: 1 }}>
							<FormControl fullWidth size="small">
								<InputLabel id="edit-user-role-label">Rol Asignado</InputLabel>
								<Select
									labelId="edit-user-role-label"
									label="Rol Asignado"
									value={editRoleId}
									onChange={(e) => setEditRoleId(e.target.value)}
								>
									<MenuItem value="">
										<em>Sin rol específico</em>
									</MenuItem>
									{roles.map((r) => (
										<MenuItem key={r.id} value={r.id}>
											{r.name} {r.is_admin ? "(Admin)" : ""}
										</MenuItem>
									))}
								</Select>
							</FormControl>

							<FormControlLabel
								control={
									<Switch
										checked={editIsEnabled}
										onChange={(e) => setEditIsEnabled(e.target.checked)}
									/>
								}
								label="Cuenta Habilitada"
							/>
						</Stack>
					</DialogContent>
					<DialogActions>
						<Button onClick={() => setOpenEdit(false)} disabled={savingUser}>
							Cancelar
						</Button>
						<Button type="submit" variant="contained" disabled={savingUser}>
							{savingUser ? "Guardando..." : "Guardar Cambios"}
						</Button>
					</DialogActions>
				</form>
			</Dialog>

			{/* Modal: Cambiar Contraseña */}
			<Dialog open={openPassword} onClose={() => setOpenPassword(false)} maxWidth="xs" fullWidth>
				<form
					onSubmit={(e) => {
						void handleChangePassword(e);
					}}
				>
					<DialogTitle>Cambiar Contraseña: {passwordUser?.username}</DialogTitle>
					<DialogContent>
						<Stack spacing={2} sx={{ mt: 1 }}>
							<TextField
								label="Nueva Contraseña"
								type="password"
								fullWidth
								required
								value={newPasswordVal}
								onChange={(e) => setNewPasswordVal(e.target.value)}
							/>
						</Stack>
					</DialogContent>
					<DialogActions>
						<Button onClick={() => setOpenPassword(false)} disabled={savingUser}>
							Cancelar
						</Button>
						<Button type="submit" variant="contained" color="warning" disabled={savingUser}>
							{savingUser ? "Actualizando..." : "Actualizar"}
						</Button>
					</DialogActions>
				</form>
			</Dialog>

			{/* Modal: Confirmar Eliminación */}
			<Dialog open={Boolean(deleteUser)} onClose={() => setDeleteUser(null)} maxWidth="xs" fullWidth>
				<DialogTitle>¿Eliminar Usuario?</DialogTitle>
				<DialogContent>
					<Typography variant="body2">
						¿Estás seguro de que deseas eliminar permanentemente al usuario{" "}
						<strong>{deleteUser?.username}</strong>? Esta acción no se puede deshacer.
					</Typography>
				</DialogContent>
				<DialogActions>
					<Button onClick={() => setDeleteUser(null)} disabled={savingUser}>
						Cancelar
					</Button>
					<Button
						onClick={() => {
							void handleDeleteUser();
						}}
						color="error"
						variant="contained"
						disabled={savingUser}
					>
						{savingUser ? "Eliminando..." : "Eliminar"}
					</Button>
				</DialogActions>
			</Dialog>
		</Box>
	);
}
