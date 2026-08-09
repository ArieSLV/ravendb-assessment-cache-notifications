import { Icon } from "components/common/Icon";
import useUniqueId from "components/hooks/useUniqueId";
import fileImporter from "common/fileImporter";

interface AceEditorLoadFileActionProps {
    onLoad: (content: string) => void;
}

export default function AceEditorLoadFileAction({ onLoad }: AceEditorLoadFileActionProps) {
    const id = useUniqueId("ace-editor-load-file-action");

    return (
        <div className="text-center">
            <label
                className="btn btn-link btn-xs text-right m-0 p-0 text-reset text-center"
                htmlFor={id}
                title="Load from a file"
            >
                <Icon icon="upload" margin="m-0" />
            </label>
            <input
                id={id}
                type="file"
                className="d-none"
                onChange={(e) => fileImporter.readAsText(e.currentTarget, onLoad)}
            />
        </div>
    );
}
