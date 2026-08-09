import Button from "react-bootstrap/Button";
import useDialog, { DialogOptions } from "../../Dialog";
import { Icon } from "../../Icon";

interface AceEditorHelpActionProps extends DialogOptions {
    tooltipTitle?: string;
}

export default function AceEditorHelpAction({ tooltipTitle = "Syntax help", ...rest }: AceEditorHelpActionProps) {
    const dialog = useDialog();

    const handleOpen = () => {
        dialog({
            actionColor: "primary",
            modalSize: "lg",
            hasNoBottomClose: true,
            hasNoHeaderPadding: true,
            ...rest,
        });
    };

    return (
        <Button variant="link" onClick={handleOpen} className="p-0 text-reset" size="sm" title={tooltipTitle}>
            <Icon icon="help" margin="m-0" />
        </Button>
    );
}
