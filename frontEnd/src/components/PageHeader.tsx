import { Box, Stack, Typography } from "@mui/material";
import type { ReactNode } from "react";

interface Props {
	title: string;
	subtitle?: string;
	actions?: ReactNode;
}

export default function PageHeader({ title, subtitle, actions }: Readonly<Props>) {
	return (
		<Box sx={{ mb: 3.5 }}>
			<Stack
				direction={{ xs: "column", sm: "row" }}
				sx={{ alignItems: { sm: "center" }, justifyContent: "space-between", gap: 2 }}
			>
				<Box sx={{ flex: 1 }}>
					<Box sx={{ display: "flex", alignItems: "center", gap: 2 }}>
						<Typography
							component="h1"
							sx={{
								fontFamily: "'Rajdhani', sans-serif",
								fontSize: { xs: "22px", sm: "26px" },
								fontWeight: 700,
								letterSpacing: "2px",
								color: "#FFFFFF",
								textTransform: "uppercase",
								lineHeight: 1.2,
							}}
						>
							{title}
						</Typography>
						<Box
							sx={{
								flex: 1,
								height: "1px",
								background: "linear-gradient(to right, #C21818 0%, rgba(194, 24, 24, 0.1) 70%, transparent 100%)",
								display: { xs: "none", sm: "block" },
							}}
						/>
					</Box>
					{subtitle && (
						<Typography
							variant="body2"
							sx={{
								fontFamily: "'Inter', sans-serif",
								fontSize: "13px",
								color: "#8E95A5",
								mt: 0.75,
							}}
						>
							{subtitle}
						</Typography>
					)}
				</Box>
				{actions && (
					<Stack direction="row" spacing={1.5} sx={{ flexShrink: 0, alignItems: "center" }}>
						{actions}
					</Stack>
				)}
			</Stack>
		</Box>
	);
}
