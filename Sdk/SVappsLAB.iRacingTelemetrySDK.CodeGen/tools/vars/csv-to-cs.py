#**
 # Copyright (C) 2024-2025 Scott Velez
 # 
 # Licensed under the Apache License, Version 2.0 (the "License");
 # you may not use this file except in compliance with the License.
 # You may obtain a copy of the License at
 # 
 # http://www.apache.org/licenses/LICENSE-2.0
 # 
 # Unless required by applicable law or agreed to in writing, software
 # distributed under the License is distributed on an "AS IS" BASIS,
 # WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 # See the License for the specific language governing permissions and
 # limitations under the License.using Microsoft.CodeAnalysis;
#**

import csv
import argparse

# Set up argument parser
parser = argparse.ArgumentParser(description='convert csv file to formatted txt file.')
parser.add_argument('input_csv', type=str, help='input csv file path')
parser.add_argument('output_txt', type=str, help='output txt file path')

args = parser.parse_args()

type_mapping = {
    'String': 0,
    'String[]': 0,
    'Boolean': 1,
    'Boolean[]': 1,
    'Int32': 2,
    'Int32[]': 2,
    'UInt32': 3,
    'UInt32[]': 3,
    'Single': 4,
    'Single[]': 4,
    'Double': 5,
    'Double[]': 5
}

# read CSV
with open(args.input_csv, mode='r', newline='') as csv_file:
    reader = csv.DictReader(csv_file)
    
    output_lines = []

    # each row
    for row in reader:
        name = row['name']
        type_ = row['type']
        length = row['length']
        isTimeValue = row['isTimeValue'].lower() == 'true'  # boolean
        desc = row['desc']
        units = row['units']

        # output type num
        output_type = type_mapping.get(type_)

        formatted_line = f'{{ "{name}", new VarItem("{name}", {output_type}, {length}, {str(isTimeValue).lower()}, "{desc}", "{units}") }},'
        output_lines.append(formatted_line)

# write
with open(args.output_txt, mode='w') as txt_file:
    txt_file.write('\n'.join(output_lines) + '\n')

print(f'Formatted output saved to {args.output_txt}')
