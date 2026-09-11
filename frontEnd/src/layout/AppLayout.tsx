import {
	AppBar,
	Box,
	Chip,
	Divider,
	Drawer,
	IconButton,
	List,
	ListItemButton,
	ListItemIcon,
	ListItemText,
	Toolbar,
	Tooltip,
	Typography,
	useMediaQuery,
	useTheme,
} from "@mui/material";
import PeopleIcon from "@mui/icons-material/People";
import AdminPanelSettingsIcon from "@mui/icons-material/AdminPanelSettings";
import CloudUploadIcon from "@mui/icons-material/CloudUpload";
import LogoutIcon from "@mui/icons-material/Logout";
import MenuIcon from "@mui/icons-material/Menu";
import DarkModeIcon from "@mui/icons-material/DarkMode";
import LightModeIcon from "@mui/icons-material/LightMode";
import { useState } from "react";
import { NavLink, Outlet, useLocation } from "react-router-dom";
import { NAV_ITEMS } from "./navItems";
import { useColorMode } from "../theme/ColorModeContext";
import { useSession } from "../auth/SessionContext";
import { URL_ACCESS } from "../api/client";

const DRAWER_WIDTH = 236;

const ICONS: Record<string, typeof PeopleIcon> = {
	People: PeopleIcon,
	AdminPanelSettings: AdminPanelSettingsIcon,
	CloudUpload: CloudUploadIcon,
};

export default function AppLayout() {
	const theme = useTheme();
	const isDesktop = useMediaQuery(theme.breakpoints.up("md"));
	const [mobileOpen, setMobileOpen] = useState(false);
	const { mode, toggle } = useColorMode();
	const { user, logout } = useSession();
	const location = useLocation();

	const current = NAV_ITEMS.find((item) => location.pathname.startsWith(item.path));

	const drawerContent = (
		<Box sx={{ display: "flex", flexDirection: "column", height: "100%" }}>
			<Toolbar sx={{ px: 2 }}>
				<Typography variant="h6" noWrap>
					DiarSpeicher
				</Typography>
			</Toolbar>
			<Divider />
			<List sx={{ px: 1, py: 1, flexGrow: 1 }}>
				{NAV_ITEMS.map((item) => {
					const Icon = ICONS[item.icon];
					return (
						<ListItemButton
							key={item.path}
							component={NavLink}
							to={item.path}
							selected={location.pathname.startsWith(item.path)}
							onClick={() => setMobileOpen(false)}
							sx={{ borderRadius: 1, mb: 0.5 }}>
							<ListItemIcon sx={{ minWidth: 38 }}>
								<Icon fontSize="small" />
							</ListItemIcon>
							<ListItemText primary={item.label} slotProps={{ primary: { sx: { fontSize: 14 } } }} />
						</ListItemButton>
					);
				})}
			</List>
			<Divider />
			<Box sx={{ p: 2 }}>
				<Typography variant="caption" color="text.secondary">
					Admin Gateway: <code>{URL_ACCESS}</code>
				</Typography>
			</Box>
		</Box>
	);

	return (
		<Box sx={{ display: "flex", height: "100vh", overflow: "hidden" }}>
			<AppBar
				position="fixed"
				color="default"
				elevation={0}
				sx={{
					borderBottom: 1,
					borderColor: "divider",
					bgcolor: "background.paper",
					width: { md: `calc(100% - ${DRAWER_WIDTH}px)` },
					ml: { md: `${DRAWER_WIDTH}px` },
				}}>
				<Toolbar>
					{!isDesktop && (
						<IconButton edge="start" onClick={() => setMobileOpen(true)} sx={{ mr: 1 }}>
							<MenuIcon />
						</IconButton>
					)}
					<Typography variant="subtitle1" sx={{ flexGrow: 1, fontWeight: 600 }}>
						{current?.label ?? "Administración"}
					</Typography>
					{user && (
						<Chip
							label={user.username}
							size="small"
							color={user.is_admin ? "primary" : "default"}
							variant="outlined"
							sx={{ mr: 1 }}
						/>
					)}
					<Tooltip title={mode === "light" ? "Tema oscuro" : "Tema claro"}>
						<IconButton onClick={toggle}>{mode === "light" ? <DarkModeIcon /> : <LightModeIcon />}</IconButton>
					</Tooltip>
					<Tooltip title="Cerrar sesión">
						<IconButton
							id="logout-btn"
							onClick={() => {
								void logout();
							}}>
							<LogoutIcon />
						</IconButton>
					</Tooltip>
				</Toolbar>
			</AppBar>

			<Box component="nav" sx={{ width: { md: DRAWER_WIDTH }, flexShrink: { md: 0 } }}>
				<Drawer
					variant={isDesktop ? "permanent" : "temporary"}
					open={isDesktop || mobileOpen}
					onClose={() => setMobileOpen(false)}
					ModalProps={{ keepMounted: true }}
					sx={{
						"& .MuiDrawer-paper": {
							width: DRAWER_WIDTH,
							boxSizing: "border-box",
							borderRight: 1,
							borderColor: "divider",
						},
					}}>
					{drawerContent}
				</Drawer>
			</Box>

			<Box
				component="main"
				sx={{
					flexGrow: 1,
					minWidth: 0,
					maxWidth: { xs: "100%", md: `calc(100% - ${DRAWER_WIDTH}px)` },
					height: "100vh",
					display: "flex",
					flexDirection: "column",
					overflow: "hidden",
				}}>
				<Toolbar />
				<Box sx={{ flex: 1, minHeight: 0, minWidth: 0, overflowY: "auto", overflowX: "hidden", p: 3 }}>
					<Outlet />
				</Box>
			</Box>
		</Box>
	);
}
