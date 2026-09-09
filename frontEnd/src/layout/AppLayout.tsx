import {
	AppBar,
	Avatar,
	Box,
	Chip,
	Container,
	IconButton,
	Stack,
	Tab,
	Tabs,
	Toolbar,
	Tooltip,
	Typography,
	useTheme,
} from "@mui/material";
import StorageIcon from "@mui/icons-material/Storage";
import Brightness4Icon from "@mui/icons-material/Brightness4";
import Brightness7Icon from "@mui/icons-material/Brightness7";
import LogoutIcon from "@mui/icons-material/Logout";
import PeopleIcon from "@mui/icons-material/People";
import SecurityIcon from "@mui/icons-material/Security";
import CloudUploadIcon from "@mui/icons-material/CloudUpload";
import { useState } from "react";
import { useSession } from "../auth/SessionContext";
import { URL_ACCESS } from "../api/client";
import UsersPage from "../pages/UsersPage";
import RolesPage from "../pages/RolesPage";
import UploadPage from "../pages/UploadPage";

interface AppLayoutProps {
	onToggleTheme: () => void;
}

export default function AppLayout({ onToggleTheme }: AppLayoutProps) {
	const theme = useTheme();
	const { user, logout } = useSession();
	const [activeTab, setActiveTab] = useState(0);

	return (
		<Box sx={{ minHeight: "100vh", backgroundColor: "background.default" }}>
			<AppBar
				position="sticky"
				elevation={0}
				sx={{
					borderBottom: 1,
					borderColor: "divider",
					backgroundColor: "background.paper",
					color: "text.primary",
				}}
			>
				<Toolbar sx={{ justifyContent: "space-between" }}>
					<Stack direction="row" spacing={1.5} sx={{ alignItems: "center" }}>
						<Box
							sx={{
								width: 38,
								height: 38,
								borderRadius: 2,
								display: "flex",
								alignItems: "center",
								justifyContent: "center",
								background:
									theme.palette.mode === "dark"
										? "linear-gradient(135deg, #6366f1 0%, #4338ca 100%)"
										: "linear-gradient(135deg, #4f46e5 0%, #3730a3 100%)",
								color: "#fff",
							}}
						>
							<StorageIcon fontSize="small" />
						</Box>
						<Box>
							<Typography variant="h6" sx={{ fontWeight: 700, lineHeight: 1.2 }}>
								DiarSpeicher
							</Typography>
							<Typography variant="caption" color="text.secondary">
								Admin Gateway: <code>{URL_ACCESS}</code>
							</Typography>
						</Box>
					</Stack>

					<Stack direction="row" spacing={2} sx={{ alignItems: "center" }}>
						{user && (
							<Stack direction="row" spacing={1} sx={{ alignItems: "center" }}>
								<Avatar
									sx={{
										width: 32,
										height: 32,
										bgcolor: "primary.main",
										fontSize: "0.875rem",
										fontWeight: 600,
									}}
								>
									{user.username.charAt(0).toUpperCase()}
								</Avatar>
								<Box sx={{ display: { xs: "none", sm: "block" } }}>
									<Typography variant="body2" sx={{ fontWeight: 600 }}>
										{user.username}
									</Typography>
									<Chip
										label={user.role || (user.is_admin ? "admin" : "reader")}
										size="small"
										color={user.is_admin ? "primary" : "default"}
										sx={{ height: 18, fontSize: "0.65rem" }}
									/>
								</Box>
							</Stack>
						)}

						<Tooltip title="Cambiar Tema (Claro / Oscuro)">
							<IconButton onClick={onToggleTheme} color="inherit" size="small">
								{theme.palette.mode === "dark" ? <Brightness7Icon fontSize="small" /> : <Brightness4Icon fontSize="small" />}
							</IconButton>
						</Tooltip>

						<Tooltip title="Cerrar Sesión">
							<IconButton
								id="logout-btn"
								onClick={() => {
									void logout();
								}}
								color="error"
								size="small"
							>
								<LogoutIcon fontSize="small" />
							</IconButton>
						</Tooltip>
					</Stack>
				</Toolbar>

				<Box sx={{ px: { xs: 2, sm: 3 } }}>
					<Tabs
						value={activeTab}
						onChange={(_: React.SyntheticEvent, val: number) => setActiveTab(val)}
						textColor="primary"
						indicatorColor="primary"
						sx={{ minHeight: 44 }}
					>
						<Tab
							id="tab-users"
							icon={<PeopleIcon fontSize="small" />}
							iconPosition="start"
							label="Usuarios"
							sx={{ minHeight: 44, py: 0 }}
						/>
						<Tab
							id="tab-roles"
							icon={<SecurityIcon fontSize="small" />}
							iconPosition="start"
							label="Roles"
							sx={{ minHeight: 44, py: 0 }}
						/>
						<Tab
							id="tab-upload"
							icon={<CloudUploadIcon fontSize="small" />}
							iconPosition="start"
							label="Subida de Ficheros"
							sx={{ minHeight: 44, py: 0 }}
						/>
					</Tabs>
				</Box>
			</AppBar>

			<Container maxWidth="xl" sx={{ py: 4 }}>
				{activeTab === 0 && <UsersPage />}
				{activeTab === 1 && <RolesPage />}
				{activeTab === 2 && <UploadPage />}
			</Container>
		</Box>
	);
}
