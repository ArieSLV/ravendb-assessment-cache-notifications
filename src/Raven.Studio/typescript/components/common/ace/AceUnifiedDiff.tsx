import diff = require("diff");
import { useEffect, useRef } from "react";
import ReactAce from "react-ace";
import ace from "ace-builds";
import AceEditor, { AceEditorProps } from "./AceEditor";

const Range = ace.require("ace/range").Range;

interface GutterMeta {
    oldNum?: number | string;
    newNum?: number | string;
    type: "add" | "remove" | "context";
}

interface AceUnifiedDiffProps extends Omit<AceEditorProps, "value" | "aceRef"> {
    value1: string;
    value2: string;
}

export default function AceUnifiedDiff({ value1, value2, ...rest }: AceUnifiedDiffProps) {
    const aceRef = useRef<ReactAce>(null);

    const diffLines = diff.diffLines(value1, value2);

    const displayLines: string[] = [];
    const gutterMeta: GutterMeta[] = [];

    let firstLineNum = 1;
    let secondLineNum = 1;

    diffLines.forEach((part) => {
        const lines = part.value.split("\n");

        // Remove last empty string if value ends with \n
        if (lines[lines.length - 1] === "") {
            lines.pop();
        }

        lines.forEach((line) => {
            if (part.added) {
                displayLines.push(line);
                gutterMeta.push({ oldNum: null, newNum: secondLineNum++, type: "add" });
            } else if (part.removed) {
                displayLines.push(line);
                gutterMeta.push({ oldNum: firstLineNum++, newNum: null, type: "remove" });
            } else {
                displayLines.push(line);
                gutterMeta.push({ oldNum: firstLineNum++, newNum: secondLineNum++, type: "context" });
            }
        });
    });

    useEffect(() => {
        if (aceRef.current) {
            const editor = aceRef.current.editor;
            editor.session.setAnnotations([]);

            // Remove previous gutter decorations and markers
            for (let i = 0; i < displayLines.length; i++) {
                editor.session.removeGutterDecoration(i, "ace_added");
                editor.session.removeGutterDecoration(i, "ace_removed");
            }
            editor.session.getMarkers(false) || {};
            Object.values(editor.session.getMarkers(false) || {}).forEach((marker: any) => {
                if (marker.class === "ace_code-added" || marker.class === "ace_code-removed") {
                    editor.session.removeMarker(marker.id);
                }
            });

            // Add new gutter decorations and line markers
            gutterMeta.forEach((meta, idx) => {
                if (meta.type === "add") {
                    editor.session.addGutterDecoration(idx, "ace_added");
                    editor.session.addMarker(new Range(idx, 0, idx, 1), "ace_code-added", "fullLine");
                } else if (meta.type === "remove") {
                    editor.session.addGutterDecoration(idx, "ace_removed");
                    editor.session.addMarker(new Range(idx, 0, idx, 1), "ace_code-removed", "fullLine");
                }
            });

            const gutterNumbersSeparator = "  ";

            // @ts-expect-error: $gutterLayer is not in the public type
            editor.session.gutterRenderer = {
                getWidth: function (session: any, lastLineNumber: any, { characterWidth }: { characterWidth: number }) {
                    const totalGutterChars =
                        firstLineNum.toString().length +
                        secondLineNum.toString().length +
                        gutterNumbersSeparator.length;

                    return totalGutterChars * characterWidth;
                },
                getText: function (_: any, row: any) {
                    const meta = gutterMeta[row];
                    if (!meta) {
                        return "";
                    }

                    return `${meta.oldNum ?? " "}${gutterNumbersSeparator}${meta.newNum ?? " "}`;
                },
            };

            editor.renderer.updateFull();
        }
    }, [aceRef, displayLines.length]);

    return (
        <AceEditor
            readOnly={true}
            aceRef={aceRef}
            value={displayLines.join("\n")}
            validationErrorMessage=""
            {...rest}
        />
    );
}
