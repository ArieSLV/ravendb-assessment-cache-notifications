import * as yup from "yup";
import genUtils from "common/generalUtils";

interface Identity {
    prefix?: string;
    value?: number;
}

export interface DocumentIdentitiesPrefixTestContext {
    isEditing: boolean;
    identities: Identity[];
}

export const documentIdentitiesSchema = yup.object({
    prefix: yup
        .string()
        .required()
        .test("not-same-prefix", (value, context) => {
            const { identities, isEditing } = context.options.context as DocumentIdentitiesPrefixTestContext;
            if (isEditing) {
                return true;
            }

            return !identities.some(
                ({ prefix }) => genUtils.normalizeString(prefix).slice(0, -1) === genUtils.normalizeString(value)
            );
        }),
    value: yup.number().integer().required(),
});

export type AddIdentitiesFormData = yup.InferType<typeof documentIdentitiesSchema>;
