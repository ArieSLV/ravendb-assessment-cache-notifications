import { UseFormSetValue, UseFormWatch } from "react-hook-form";
import { EditDocumentRevisionsCollectionConfig } from "./DocumentRevisionsValidation";
import { useEffect } from "react";

export default function useEditRevisionFormSideEffects(
    watch: UseFormWatch<EditDocumentRevisionsCollectionConfig>,
    setValue: UseFormSetValue<EditDocumentRevisionsCollectionConfig>
) {
    useEffect(() => {
        const { unsubscribe } = watch((values, { name }) => {
            if (name === "isMinimumRevisionAgeToKeepEnabled" && !values.isMinimumRevisionAgeToKeepEnabled) {
                setValue("minimumRevisionAgeToKeep", null, { shouldValidate: true });
            }

            if (name === "isMinimumRevisionsToKeepEnabled" && !values.isMinimumRevisionsToKeepEnabled) {
                setValue("minimumRevisionsToKeep", null, { shouldValidate: true });
            }

            if (name === "isMinimumRevisionAgeToKeepEnabled" || name === "isMinimumRevisionsToKeepEnabled") {
                if (!values.isMinimumRevisionAgeToKeepEnabled && !values.isMinimumRevisionsToKeepEnabled) {
                    setValue("isMaximumRevisionsToDeleteUponDocumentUpdateEnabled", false, { shouldValidate: true });
                }
            }

            if (
                name === "isMaximumRevisionsToDeleteUponDocumentUpdateEnabled" &&
                !values.isMaximumRevisionsToDeleteUponDocumentUpdateEnabled
            ) {
                setValue("maximumRevisionsToDeleteUponDocumentUpdate", null, { shouldValidate: true });
            }
        });
        return () => unsubscribe();
    }, [setValue, watch]);
}
