export interface NavItem {
	path: string;
	label: string;
	icon: string;
}

export const NAV_ITEMS: NavItem[] = [
	{ path: "/users", label: "Usuarios", icon: "People" },
	{ path: "/roles", label: "Roles", icon: "AdminPanelSettings" },
	{ path: "/upload", label: "Subida de ficheros", icon: "CloudUpload" },
];
