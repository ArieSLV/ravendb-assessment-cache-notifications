import { Icon } from "components/common/Icon";
import Button from "react-bootstrap/Button";

interface AceEditorDeleteActionProps {
    onDelete: () => void;
}

export default function AceEditorDeleteAction({ onDelete }: AceEditorDeleteActionProps) {
    return (
        <Button variant="link" onClick={onDelete} className="p-0 text-reset" size="sm" title="Delete">
            <Icon icon="trash" margin="m-0" />
        </Button>
    );
}
