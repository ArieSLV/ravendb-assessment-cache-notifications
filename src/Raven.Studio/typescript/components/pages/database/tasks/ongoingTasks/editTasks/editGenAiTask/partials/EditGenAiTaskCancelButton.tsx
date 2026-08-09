import Button from "react-bootstrap/Button";
import useEditGenAiCancel from "../hooks/useEditGenAiCancel";

export default function EditGenAiTaskCancelButton() {
    const cancel = useEditGenAiCancel();

    return (
        <Button
            variant="outline-secondary rounded-pill"
            onClick={cancel}
            title="Cancel configuration and return to the tasks list."
        >
            Cancel
        </Button>
    );
}
