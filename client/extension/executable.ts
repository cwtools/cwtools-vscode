// Vendored and adapted from https://github.com/kevva/executable, Copyright (c) Kevin Mårtensson <kevinmartensson@gmail.com>
'use strict';

import { access, constants, stat } from "fs/promises";

const isExe = async (name : string) => {
    try {
        await access(name, constants.X_OK)
        return true;
    }
    catch(_)
    {
        return false;
    }
};


export async function existAndIsExe(name : string) {
    if (typeof name !== 'string') {
        throw new TypeError('Expected a string');
    }

    try
    {
        const stats = await stat(name);
        return stats && stats.isFile() && isExe(name);
    }
    catch(_)
    {
        return false;
    }

};
