import { Box, Stack, Typography } from "@mui/material";
import type { ReactNode } from "react";

interface Props {
	title: string;
	subtitle?: string;
	actions?: ReactNode;
}

export default function PageHeader({ title, subtitle, actions }: Readonly<Props>) {
	return (
		<Stack direction="row" sx={{ mb: 3, alignItems: "flex-start", justifyContent: "space-between" }} spacing={2}>
			<Box>
				<Typography variant="h5">{title}</Typography>
				{subtitle && (
					<Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
						{subtitle}
					</Typography>
				)}
			</Box>
			{actions && (
				<Stack direction="row" spacing={1}>
					{actions}
				</Stack>
			)}
		</Stack>
	);
}
