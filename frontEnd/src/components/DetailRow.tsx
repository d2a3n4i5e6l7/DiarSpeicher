import { Box, Typography } from "@mui/material";
import { DS } from "../theme";

export default function DetailRow({
	label,
	value,
}: Readonly<{ label: string; value: string | null | undefined }>) {
	if (!value) return null;
	return (
		<Box sx={{ display: "flex", gap: 2, py: 1, borderBottom: `1px solid ${DS.borderSoft}` }}>
			<Typography
				sx={{
					fontFamily: "'Rajdhani', sans-serif",
					fontWeight: 700,
					fontSize: "12px",
					letterSpacing: "1.5px",
					textTransform: "uppercase",
					color: DS.muted,
					minWidth: 150,
					flexShrink: 0,
				}}
			>
				{label}
			</Typography>
			<Typography
				sx={{ fontFamily: "'JetBrains Mono', monospace", fontSize: "12px", color: DS.platinum, wordBreak: "break-all" }}
			>
				{value}
			</Typography>
		</Box>
	);
}
