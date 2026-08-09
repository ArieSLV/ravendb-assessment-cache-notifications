import { useServices } from "components/hooks/useServices";
import { UseFormSetError } from "react-hook-form";
import { CreateDatabaseRegularFormData } from "../regular/createDatabaseRegularValidation";
import { CreateDatabaseFromBackupFormData } from "../formBackup/createDatabaseFromBackupValidation";
import { useAsyncDebounce } from "components/hooks/useAsyncDebounce";

export const useCreateDatabaseAsyncValidation = (
    databaseName: string,
    setError: UseFormSetError<CreateDatabaseRegularFormData | CreateDatabaseFromBackupFormData>
) => {
    const { resourcesService } = useServices();

    return useAsyncDebounce(
        async () => {
            if (!databaseName) {
                return true; // It will be validated by the form
            }

            const result = await resourcesService.validateName("Database", databaseName ?? "");
            if (!result.IsValid) {
                setError("basicInfoStep.databaseName", {
                    type: "manual",
                    message: result.ErrorMessage,
                });
            }

            return result.IsValid;
        },
        [databaseName],
        500
    );
};
