import {
	Box,
	Chip,
	Divider,
	IconButton,
	LinearProgress,
	ListItem,
	Stack,
	Typography,
} from "@mui/material";
import DeleteOutlinedIcon from "@mui/icons-material/DeleteOutlined";
import InsertDriveFileIcon from "@mui/icons-material/InsertDriveFile";
import PauseOutlinedIcon from "@mui/icons-material/PauseOutlined";
import PlayArrowOutlinedIcon from "@mui/icons-material/PlayArrowOutlined";
import React from "react";
import { formatBytes } from "../catalog/bytes";
import type { TusUpload, TusUploadStatus } from "../api/tusClient";

export interface UploadQueueItem {
	id: string;
	file: File;
	tusUpload: TusUpload;
	status: TusUploadStatus;
	bytesUploaded: number;
	percentage: number;
	errorMessage?: string;
}

interface UploadItemRowProps {
	item: UploadQueueItem;
	showDivider: boolean;
	onPause: (item: UploadQueueItem) => void;
	onStart: (item: UploadQueueItem) => void;
	onRemove: (item: UploadQueueItem) => void;
}

function getStatusMeta(status: TusUploadStatus, percentage: number): {
	color: "default" | "primary" | "warning" | "success" | "error";
	text: string;
} {
	if (status === "uploading") {
		return { color: "primary", text: `Subiendo (${percentage}%)` };
	}
	if (status === "paused") {
		return { color: "warning", text: `Pausado (${percentage}%)` };
	}
	if (status === "completed") {
		return { color: "success", text: "Completado" };
	}
	if (status === "error") {
		return { color: "error", text: "Error" };
	}
	return { color: "default", text: "En cola" };
}

export default function UploadItemRow({
	item,
	showDivider,
	onPause,
	onStart,
	onRemove,
}: Readonly<UploadItemRowProps>) {
	const meta = getStatusMeta(item.status, item.percentage);
	const canStart = item.status === "paused" || item.status === "idle" || item.status === "error";

	return (
		<React.Fragment>
			{showDivider && <Divider />}
			<ListItem sx={{ py: 1.5, display: "block" }}>
				<Stack spacing={1}>
					<Stack direction="row" sx={{ alignItems: "center", justifyContent: "space-between" }}>
						<Stack direction="row" spacing={1} sx={{ alignItems: "center", minWidth: 0 }}>
							<InsertDriveFileIcon color="action" fontSize="small" />
							<Typography variant="body2" sx={{ fontWeight: 600 }} noWrap>
								{item.file.name}
							</Typography>
							<Typography variant="caption" color="text.secondary">
								({formatBytes(item.file.size)})
							</Typography>
						</Stack>

						<Stack direction="row" spacing={1} sx={{ alignItems: "center" }}>
							<Chip
								label={meta.text}
								size="small"
								color={meta.color}
								variant={item.status === "idle" ? "outlined" : "filled"}
							/>

							{item.status === "uploading" && (
								<IconButton
									size="small"
									color="warning"
									title="Pausar subida"
									onClick={() => {
										onPause(item);
									}}
								>
									<PauseOutlinedIcon fontSize="small" />
								</IconButton>
							)}

							{canStart && (
								<IconButton
									size="small"
									color="primary"
									title="Reanudar o Iniciar subida"
									onClick={() => {
										onStart(item);
									}}
								>
									<PlayArrowOutlinedIcon fontSize="small" />
								</IconButton>
							)}

							<IconButton
								size="small"
								color="error"
								title="Cancelar y eliminar"
								onClick={() => {
									onRemove(item);
								}}
							>
								<DeleteOutlinedIcon fontSize="small" />
							</IconButton>
						</Stack>
					</Stack>

					<Box sx={{ width: "100%" }}>
						<LinearProgress
							variant="determinate"
							value={item.percentage}
							color={item.status === "error" ? "error" : "primary"}
							sx={{ height: 6, borderRadius: 3 }}
						/>
						<Stack direction="row" sx={{ justifyContent: "space-between", mt: 0.5 }}>
							<Typography variant="caption" color="text.secondary">
								{formatBytes(item.bytesUploaded)} / {formatBytes(item.file.size)}
							</Typography>
							<Typography variant="caption" color="text.secondary">
								{item.percentage}%
							</Typography>
						</Stack>
					</Box>

					{item.errorMessage && (
						<Typography variant="caption" color="error">
							{item.errorMessage}
						</Typography>
					)}
				</Stack>
			</ListItem>
		</React.Fragment>
	);
}
