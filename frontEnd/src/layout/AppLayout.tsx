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
import LibraryBooksIcon from "@mui/icons-material/LibraryBooks";
import HomeIcon from "@mui/icons-material/Home";
import CollectionsBookmarkIcon from "@mui/icons-material/CollectionsBookmark";
import StorageIcon from "@mui/icons-material/Storage";
import LogoutIcon from "@mui/icons-material/Logout";
import MenuIcon from "@mui/icons-material/Menu";
import DarkModeIcon from "@mui/icons-material/DarkMode";
import LightModeIcon from "@mui/icons-material/LightMode";
import React, { useState } from "react";
import { NavLink, Outlet, useLocation } from "react-router-dom";
import { NAV_GROUPS, NAV_ITEMS, isNavItemActive } from "./navItems";
import LibrarySelector from "./LibrarySelector";
import { useColorMode } from "../theme/ColorModeContext";
import { useSession } from "../auth/SessionContext";
import { URL_ACCESS } from "../api/client";
import DiarSpeicherLogo from "../components/DiarSpeicherLogo";

const DRAWER_WIDTH = 250;

const ICONS: Record<string, typeof PeopleIcon> = {
	People: PeopleIcon,
	AdminPanelSettings: AdminPanelSettingsIcon,
	CloudUpload: CloudUploadIcon,
	LibraryBooks: LibraryBooksIcon,
	Home: HomeIcon,
	CollectionsBookmark: CollectionsBookmarkIcon,
	Storage: StorageIcon,
};

export default function AppLayout() {
	const theme = useTheme();
	const isDesktop = useMediaQuery(theme.breakpoints.up("md"));
	const [mobileOpen, setMobileOpen] = useState(false);
	const { mode, toggle } = useColorMode();
	const { user, logout } = useSession();
	const location = useLocation();

	// La coincidencia más larga gana: con "/" en la lista, startsWith casaría siempre con ella.
	const current = [...NAV_ITEMS]
		.sort((a, b) => b.path.length - a.path.length)
		.find((item) => isNavItemActive(item, location.pathname));
	const currentTitle = current?.label ?? "Archivo DiarSpeicher";

	let userBadge: React.ReactNode = null;
	if (user) {
		const rolePrefix = user.is_admin ? "SYSADMIN" : "READER";
		userBadge = (
			<Chip
				label={`${rolePrefix} // ${user.username}`}
				size="small"
				sx={{
					fontFamily: "'JetBrains Mono', monospace",
					fontSize: "11px",
					fontWeight: 700,
					backgroundColor: "#11131C",
					color: user.is_admin ? "#FF2E2E" : "#F0F2F6",
					border: `1px solid ${user.is_admin ? "#C21818" : "#282C38"}`,
					borderRadius: "2px",
					mr: 1.5,
					px: 0.5,
				}}
			/>
		);
	}

	let themeToggleIcon = <LightModeIcon fontSize="small" />;
	let themeTooltip = "Tema claro";
	if (mode === "light") {
		themeToggleIcon = <DarkModeIcon fontSize="small" />;
		themeTooltip = "Tema oscuro táctico";
	}

	const drawerContent = (
		<Box
			sx={{
				display: "flex",
				flexDirection: "column",
				height: "100%",
				backgroundColor: "#0A0B0E",
				color: "#F0F2F6",
			}}
		>
			<Toolbar
				sx={{
					px: 2.5,
					borderBottom: "1px solid #232733",
					display: "flex",
					alignItems: "center",
					justifyContent: "flex-start",
					minHeight: "64px !important",
				}}
			>
				<DiarSpeicherLogo size={32} showSubtitle subtitle="ARCHIVE PROTOCOL" />
			</Toolbar>

			<Box sx={{ flexGrow: 1, overflowY: "auto" }}>
				{NAV_GROUPS.map((group) => (
					<Box key={group.section}>
						<Box sx={{ px: 2, pt: 2.5, pb: 1 }}>
							<Typography
								sx={{
									fontFamily: "'Rajdhani', sans-serif",
									fontWeight: 700,
									fontSize: "11px",
									letterSpacing: "2px",
									color: "#636B7C",
									textTransform: "uppercase",
								}}
							>
								{group.title}
							</Typography>
						</Box>

						<List sx={{ px: 1.5, py: 0.5 }}>
							{group.items.map((item) => {
								const Icon = ICONS[item.icon];
								const isSelected = isNavItemActive(item, location.pathname);

								return (
									<ListItemButton
										key={item.path}
										component={NavLink}
										to={item.path}
										selected={isSelected}
										onClick={() => setMobileOpen(false)}
										sx={{
											borderRadius: "2px",
											mb: 0.75,
											py: 1,
											px: 1.5,
											transition: "all 0.15s ease",
											borderLeft: isSelected ? "3px solid #C21818" : "3px solid transparent",
											backgroundColor: isSelected ? "rgba(194, 24, 24, 0.12) !important" : "transparent",
											color: isSelected ? "#FFFFFF" : "#A3ABB8",
											"&:hover": {
												backgroundColor: "rgba(194, 24, 24, 0.08)",
												color: "#FFFFFF",
												"& .MuiListItemIcon-root": {
													color: "#FF2E2E",
												},
											},
										}}
									>
										<ListItemIcon
											sx={{
												minWidth: 36,
												color: isSelected ? "#FF2E2E" : "#7B8496",
												transition: "color 0.15s ease",
											}}
										>
											<Icon fontSize="small" />
										</ListItemIcon>
										<ListItemText
											primary={item.label}
											slotProps={{
												primary: {
													sx: {
														fontFamily: "'Rajdhani', sans-serif",
														fontWeight: isSelected ? 700 : 600,
														fontSize: "14px",
														letterSpacing: "0.5px",
														textTransform: "uppercase",
													},
												},
											}}
										/>
									</ListItemButton>
								);
							})}
						</List>
					</Box>
				))}
			</Box>

			<Divider sx={{ borderColor: "#1C1F28" }} />

			<Box sx={{ p: 2, backgroundColor: "#07080A" }}>
				<Box sx={{ display: "flex", alignItems: "center", gap: 1, mb: 0.5 }}>
					<Box
						sx={{
							width: 6,
							height: 6,
							borderRadius: "50%",
							backgroundColor: "#22C55E",
							boxShadow: "0 0 6px #22C55E",
						}}
					/>
					<Typography
						sx={{
							fontFamily: "'JetBrains Mono', monospace",
							fontSize: "10px",
							color: "#8E95A5",
						}}
					>
						NODE: ONLINE
					</Typography>
				</Box>
				<Typography
					variant="caption"
					sx={{
						fontFamily: "'JetBrains Mono', monospace",
						fontSize: "10px",
						color: "#636B7C",
						display: "block",
						overflow: "hidden",
						textOverflow: "ellipsis",
						whiteSpace: "nowrap",
					}}
				>
					GW: <code>{URL_ACCESS}</code>
				</Typography>
			</Box>
		</Box>
	);

	return (
		<Box sx={{ display: "flex", height: "100vh", overflow: "hidden", backgroundColor: "#050508" }}>
			<AppBar
				position="fixed"
				elevation={0}
				sx={{
					borderBottom: "1px solid #232733",
					background: "linear-gradient(90deg, #1C0303 0%, #050508 100%)",
					width: { md: `calc(100% - ${DRAWER_WIDTH}px)` },
					ml: { md: `${DRAWER_WIDTH}px` },
					zIndex: (appBarTheme) => appBarTheme.zIndex.drawer + 1,
				}}
			>
				<Toolbar sx={{ minHeight: "64px !important", px: { xs: 2, sm: 3 } }}>
					{!isDesktop && (
						<IconButton
							edge="start"
							onClick={() => setMobileOpen(true)}
							sx={{ mr: 1.5, color: "#F0F2F6" }}
						>
							<MenuIcon />
						</IconButton>
					)}

					<Box sx={{ flexGrow: 1, display: "flex", alignItems: "center", gap: 1.5 }}>
						<Typography
							variant="subtitle1"
							sx={{
								fontFamily: "'Rajdhani', sans-serif",
								fontWeight: 700,
								fontSize: "18px",
								letterSpacing: "1.5px",
								textTransform: "uppercase",
								color: "#FFFFFF",
							}}
						>
							{currentTitle}
						</Typography>
						<Box
							sx={{
								px: 1,
								py: 0.25,
								backgroundColor: "#11131C",
								border: "1px solid #282C38",
								borderRadius: "2px",
								display: { xs: "none", sm: "block" },
							}}
						>
							<Typography
								sx={{
									fontFamily: "'JetBrains Mono', monospace",
									fontSize: "10px",
									color: "#8E95A5",
								}}
							>
								DS-NODE-01
							</Typography>
						</Box>
					</Box>

					<LibrarySelector />

					{userBadge}

					<Tooltip title={themeTooltip}>
						<IconButton
							onClick={toggle}
							sx={{
								color: "#A3ABB8",
								border: "1px solid #282C38",
								borderRadius: "2px",
								p: 0.75,
								mr: 1,
								"&:hover": { color: "#FFFFFF", borderColor: "#C21818" },
							}}
						>
							{themeToggleIcon}
						</IconButton>
					</Tooltip>

					<Tooltip title="Cerrar sesión de nodo">
						<IconButton
							id="logout-btn"
							onClick={() => {
								void logout();
							}}
							sx={{
								color: "#A3ABB8",
								border: "1px solid #282C38",
								borderRadius: "2px",
								p: 0.75,
								"&:hover": { color: "#FF2E2E", borderColor: "#C21818", backgroundColor: "rgba(194, 24, 24, 0.1)" },
							}}
						>
							<LogoutIcon fontSize="small" />
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
							borderRight: "1px solid #232733",
						},
					}}
				>
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
					position: "relative",
					backgroundColor: "#050508",
					/* Rejilla tactica y halo carmesi: fijos, no arrastran con el scroll. */
					"&::before": {
						content: '""',
						position: "absolute",
						inset: 0,
						backgroundImage:
							"linear-gradient(to right, rgba(194, 24, 24, 0.03) 1px, transparent 1px), linear-gradient(to bottom, rgba(194, 24, 24, 0.03) 1px, transparent 1px)",
						backgroundSize: "40px 40px",
						pointerEvents: "none",
						zIndex: 0,
					},
					"&::after": {
						content: '""',
						position: "absolute",
						inset: 0,
						backgroundImage:
							"radial-gradient(circle at 50% 0%, rgba(194, 24, 24, 0.08) 0%, transparent 55%)",
						pointerEvents: "none",
						zIndex: 0,
					},
				}}
			>
				<Toolbar sx={{ minHeight: "64px !important" }} />
				<Box
					sx={{
						flex: 1,
						minHeight: 0,
						minWidth: 0,
						overflowY: "auto",
						overflowX: "hidden",
						position: "relative",
						zIndex: 1,
						p: { xs: 2, sm: 3.5 },
					}}
				>
					<Outlet />
				</Box>
			</Box>
		</Box>
	);
}
