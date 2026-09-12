import {
	Alert,
	Button,
	Dialog,
	DialogActions,
	DialogContent,
	DialogTitle,
	Stack,
	TextField,
} from "@mui/material";
import { useState } from "react";
import HudFrame from "./HudFrame";
import { seriesApi } from "../api/endpoints";
import { DS } from "../theme";

interface Props {
	open: boolean;
	seriesId: string;
	currentName: string;
	currentDescription?: string;
	onClose: () => void;
	onSaved: () => void;
}

export default function RenameSeriesDialog({
	open,
	seriesId,
	currentName,
	currentDescription,
	onClose,
	onSaved,
}: Readonly<Props>) {
	const [name, setName] = useState(currentName);
	const [description, setDescription] = useState(currentDescription ?? "");
	const [saving, setSaving] = useState(false);
	const [error, setError] = useState<string | null>(null);

	const submit = async (e: React.SyntheticEvent) => {
		e.preventDefault();
		if (!name.trim()) {
			setError("El nombre no puede quedar vacío.");
			return;
		}

		setSaving(true);
		setError(null);
		try {
			await seriesApi.update(seriesId, { name: name.trim(), description });
			onSaved();
			onClose();
		} catch (err: unknown) {
			setError(err instanceof Error ? err.message : "No se pudo guardar la serie.");
		} finally {
			setSaving(false);
		}
	};

	return (
		// keepMounted queda fuera: el diálogo siembra su estado del nombre actual al
		// montarse, y mantenerlo vivo dejaría el formulario con el nombre viejo.
		<Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth>
			<HudFrame />
			<form
				onSubmit={(e) => {
					void submit(e);
				}}
			>
				<DialogTitle>Editar serie</DialogTitle>

				<DialogContent sx={{ p: 3 }}>
					{error && (
						<Alert severity="error" sx={{ mb: 2 }}>
							{error}
						</Alert>
					)}

					<Stack spacing={2.5} sx={{ mt: 1 }}>
						<TextField
							label="Nombre de la serie"
							value={name}
							onChange={(e) => setName(e.target.value)}
							fullWidth
							required
							autoFocus
							disabled={saving}
							helperText="Por defecto es el nombre de la carpeta. Al cambiarlo, los rescaneos lo respetan."
						/>
						<TextField
							label="Descripción"
							value={description}
							onChange={(e) => setDescription(e.target.value)}
							fullWidth
							multiline
							rows={3}
							disabled={saving}
						/>
					</Stack>
				</DialogContent>

				<DialogActions>
					<Button onClick={onClose} disabled={saving} sx={{ color: DS.muted }}>
						CANCELAR
					</Button>
					<Button type="submit" variant="contained" disabled={saving} sx={{ backgroundColor: DS.red }}>
						{saving ? "GUARDANDO..." : "GUARDAR"}
					</Button>
				</DialogActions>
			</form>
		</Dialog>
	);
}
