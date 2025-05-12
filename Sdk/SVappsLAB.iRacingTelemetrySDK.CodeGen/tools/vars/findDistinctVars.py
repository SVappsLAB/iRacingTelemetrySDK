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
import glob
import argparse
import sys

parser = argparse.ArgumentParser(description='process csv files based on a glob pattern.')
parser.add_argument('path', type=str, help='glob pattern for matching csv files (e.g., /path/to/*.csv)')
parser.add_argument('output', type=str, help='output csv file name')

args = parser.parse_args()

path = args.path
output_file = args.output

csv_files = glob.glob(path)

# dictionary of rows
unique_rows = {}

# read and process all CSV files
for file in csv_files:
    with open(file, mode='r', newline='') as f:
        reader = csv.reader(f)
        headers = next(reader)
        headers = [header.strip() for header in headers]  # strip whitespace from headers

        reader = csv.DictReader(f, fieldnames=headers)

        for row in reader:
            name = row['name']  # use 'name' column to check for duplicates
            if name not in unique_rows:
                unique_rows[name] = row  # store row if 'name' is not already in dictionary

# sort
sorted_rows = sorted(unique_rows.values(), key=lambda row: row['name'])


# header order
header_order = ['name', 'type', 'length', 'isTimeValue', 'desc', 'units']

# write distinct rows to the output CSV
with open(output_file, mode='w', newline='') as f:
    if sorted_rows:
        writer = csv.DictWriter(f, fieldnames=header_order)
        writer.writeheader()
        writer.writerows(sorted_rows)

print(f'distinct sorted data saved to {output_file}')
